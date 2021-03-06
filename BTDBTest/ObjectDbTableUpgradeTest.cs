using System;
using System.Collections.Generic;
using BTDB.KVDBLayer;
using BTDB.ODBLayer;
using Xunit;

namespace BTDBTest
{
    public class ObjectDbTableUpgradeTest : IDisposable
    {
        readonly IKeyValueDB _lowDb;
        IObjectDB _db;

        public ObjectDbTableUpgradeTest()
        {
            _lowDb = new InMemoryKeyValueDB();
            OpenDb();
        }

        public void Dispose()
        {
            _db.Dispose();
            _lowDb.Dispose();
        }

        void ReopenDb()
        {
            _db.Dispose();
            OpenDb();
        }

        void OpenDb()
        {
            _db = new ObjectDB();
            _db.Open(_lowDb, false, new DBOptions().WithoutAutoRegistration());
        }

        public class JobV1
        {
            [PrimaryKey(1)]
            public ulong Id { get; set; }

            public string Name { get; set; }
        }

        public interface IJobTable1
        {
            void Insert(JobV1 job);
        }

        public class JobV2
        {
            [PrimaryKey(1)]
            public ulong Id { get; set; }

            [SecondaryKey("Name")]
            public string Name { get; set; }

            [SecondaryKey("Cost", IncludePrimaryKeyOrder = 1)]
            public uint Cost { get; set; }
        }

        public interface IJobTable2
        {
            void Insert(JobV2 job);
            JobV2 FindByNameOrDefault(string name);
            JobV2 FindByCostOrDefault(ulong id, uint cost);
            IEnumerator<JobV2> ListByCost(AdvancedEnumeratorParam<uint> param);
        }

        public class JobV3
        {
            [PrimaryKey(1)]
            public ulong Id { get; set; }

            [SecondaryKey("Cost")]
            public double Cost { get; set; }
        }

        public class JobIncompatible
        {
            [PrimaryKey(1)]
            public Guid Id { get; set; }
        }

        public interface IJobTableIncompatible
        {
            void Insert(JobIncompatible job);
        }

        [Fact]
        public void ChangeOfPrimaryKeyIsNotSupported()
        {
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<IJobTable1>("Job");
                var jobTable = creator(tr);
                var job = new JobV1 { Id = 11, Name = "Code" };
                jobTable.Insert(job);
                tr.Commit();
            }
            ReopenDb();
            using (var tr = _db.StartTransaction())
            {
                Assert.Throws<BTDBException>(() => tr.InitRelation<IJobTableIncompatible>("Job"));
            }
        }

        [Fact]
        public void NewIndexesAreAutomaticallyGenerated()
        {
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<IJobTable1>("Job");
                var jobTable = creator(tr);
                var job = new JobV1 { Id = 11, Name = "Code" };
                jobTable.Insert(job);
                tr.Commit();
            }
            ReopenDb();
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<IJobTable2>("Job");
                var jobTable = creator(tr);
                var job = new JobV2 { Id = 21, Name = "Build", Cost = 42 };
                jobTable.Insert(job);
                var j = jobTable.FindByNameOrDefault("Code");
                Assert.Equal("Code", j.Name);
                j = jobTable.FindByCostOrDefault(21, 42);
                Assert.Equal("Build", j.Name);

                var en = jobTable.ListByCost(new AdvancedEnumeratorParam<uint>(EnumerationOrder.Ascending));
                Assert.True(en.MoveNext());
                Assert.Equal(0u, en.Current.Cost);
                Assert.True(en.MoveNext());
                Assert.Equal(42u, en.Current.Cost);
                tr.Commit();
            }
        }

        public class Car
        {
            [PrimaryKey(1)]
            public ulong CompanyId { get; set; }
            [PrimaryKey(2)]
            public ulong Id { get; set; }
            public string Name { get; set; }
        }

        public interface ICarTableApart
        {
            ulong CompanyId { get; set; }
            void Insert(Car car);
            Car FindById(ulong id);
        }

        public interface ICarTable
        {
            void Insert(Car car);
            Car FindById(ulong companyId, ulong id);
        }

        [Fact]
        public void ApartFieldCanBeRemoved()
        {
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ICarTableApart>("Car");
                var carTable = creator(tr);
                carTable.CompanyId = 10;
                var car = new Car { Id = 11, Name = "Ferrari" };
                carTable.Insert(car);
                tr.Commit();
            }
            ReopenDb();
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ICarTable>("Car");
                var carTable = creator(tr);
                Assert.Equal("Ferrari", carTable.FindById(10, 11).Name);
            }
        }

        [Fact]
        public void ApartFieldCanBeAdded()
        {
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ICarTable>("Car");
                var carTable = creator(tr);
                var car = new Car { CompanyId = 10, Id = 11, Name = "Ferrari" };
                carTable.Insert(car);
                tr.Commit();
            }
            ReopenDb();
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ICarTableApart>("Car");
                var carTable = creator(tr);
                carTable.CompanyId = 10;
                Assert.Equal("Ferrari", carTable.FindById(11).Name);
            }
        }

        public enum SimpleEnum
        {
            One = 1,
            Two = 2
        }

        public enum SimpleEnumV2
        {
            Eins = 1,
            Zwei = 2,
            Drei = 3
        }

        public enum SimpleEnumV3
        {
            Two = 2,
            Three = 3,
            Four = 4

        }

        public class ItemWithEnumInKey
        {
            [PrimaryKey]
            public SimpleEnum Key { get; set; }
            public string Value { get; set; }
        }

        public class ItemWithEnumInKeyV2
        {
            [PrimaryKey]
            public SimpleEnumV2 Key { get; set; }
            public string Value { get; set; }
        }

        public class ItemWithEnumInKeyV3
        {
            [PrimaryKey]
            public SimpleEnumV3 Key { get; set; }
            public string Value { get; set; }
        }

        public interface ITableWithEnumInKey : IReadOnlyCollection<ItemWithEnumInKey>
        {
            void Insert(ItemWithEnumInKey person);
        }

        public interface ITableWithEnumInKeyV2 : IReadOnlyCollection<ItemWithEnumInKeyV2>
        {
            bool Upsert(ItemWithEnumInKeyV2 person);
            ItemWithEnumInKeyV2 FindById(SimpleEnumV2 key);
        }

        public interface ITableWithEnumInKeyV3 : IReadOnlyCollection<ItemWithEnumInKeyV3>
        {
            bool Upsert(ItemWithEnumInKeyV3 person);
        }

        [Fact]
        public void UpgradePrimaryKeyWithEnumWorks()
        {
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ITableWithEnumInKey>("EnumWithItemInKey");
                var table = creator(tr);

                table.Insert(new ItemWithEnumInKey { Key = SimpleEnum.One, Value = "A" });
                table.Insert(new ItemWithEnumInKey { Key = SimpleEnum.Two, Value = "B" });

                tr.Commit();
            }
            ReopenDb();
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ITableWithEnumInKeyV2>("EnumWithItemInKey");
                var table = creator(tr);
                Assert.Equal("A", table.FindById(SimpleEnumV2.Eins).Value);
                Assert.False(table.Upsert(new ItemWithEnumInKeyV2 { Key = SimpleEnumV2.Zwei, Value = "B2" }));
                Assert.True(table.Upsert(new ItemWithEnumInKeyV2 { Key = SimpleEnumV2.Drei, Value = "C" }));
                Assert.Equal(3, table.Count);
            }
        }

        [Fact]
        public void UpgradePrimaryKeyWithIncompatibleEnumNotWork()
        {
            using (var tr = _db.StartTransaction())
            {
                var creator = tr.InitRelation<ITableWithEnumInKey>("EnumWithItemInKeyIncompatible");
                var table = creator(tr);

                table.Insert(new ItemWithEnumInKey { Key = SimpleEnum.One, Value = "A" });

                tr.Commit();
            }
            ReopenDb();
            using (var tr = _db.StartTransaction())
            {
                var ex = Assert.Throws<BTDBException>(() => tr.InitRelation<ITableWithEnumInKeyV3>("EnumWithItemInKeyIncompatible"));
                Assert.Contains("Field 'Key'", ex.Message);
            }
        }

    }
}