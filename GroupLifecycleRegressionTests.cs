using System;
using System.IO;

static class GroupLifecycleRegressionTests
{
    static int _assertions;
    static void Assert(bool condition,string message){_assertions++;if(!condition)throw new InvalidOperationException($"GROUP LIFECYCLE TEST FAILED: {message}");}
    public static void Run()
    {
        string db=Path.Combine(Path.GetTempPath(),$"alice-group-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("ALICE_DB_PATH",db);
        try
        {
            Database.Init();const long chat=-93001,owner=930;
            Database.AddCountry(new Country{OwnerId=owner,ChatId=chat,Name="GroupCountry",OwnerName="Owner",Faction=Faction.USA,
                Population=100_000,Soldiers=10_000,Money=10_000,Cities=4});
            Assert(Database.IsBotGroupActive(chat),"unknown legacy group must default active until a removal update is observed");
            Assert(Database.GetUserActiveChatIds(owner).Contains(chat),"active group must be available to private operations");
            Database.SetBotGroupActive(chat,false);
            Assert(!Database.IsBotGroupActive(chat),"removed group must become inactive");
            Assert(!Database.GetUserActiveChatIds(owner).Contains(chat),"inactive group must disappear from private operation choices");
            Database.SetBotGroupActive(chat,true);
            Assert(Database.IsBotGroupActive(chat)&&Database.GetUserActiveChatIds(owner).Contains(chat),"re-added group must reactivate");
            Console.WriteLine($"GROUP LIFECYCLE REGRESSION TESTS PASSED — {_assertions} assertions");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach(string suffix in new[]{"","-wal","-shm"})try{File.Delete(db+suffix);}catch{}
            Environment.SetEnvironmentVariable("ALICE_DB_PATH",null);
        }
    }
}
