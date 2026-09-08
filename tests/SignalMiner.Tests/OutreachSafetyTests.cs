using Microsoft.EntityFrameworkCore;
using Npgsql;
using SignalMiner.Application;
using SignalMiner.Domain;
using SignalMiner.Infrastructure;
using Xunit;

namespace SignalMiner.Tests;

public sealed class SafetyDatabaseFactAttribute : FactAttribute
{
    public SafetyDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIGNALMINER_TEST_DB")))
            Skip = "Set SIGNALMINER_TEST_DB to run isolated PostgreSQL safety tests.";
    }
}

public sealed class OutreachSafetyTests
{
    [Fact]
    public async Task ExhaustedLimitReturnsHttp429AndRemainingCapacity()
    {
        var controller = new SignalMiner.Api.Controllers.LeadsController(null!, null!, new ExhaustedSender(), new TestSuppressions());
        var result = await controller.SendManualEmail(Guid.NewGuid(), "a@example.com", "Hello", "Body", null,
            null, null, null, false, false, null, null, null, null, null, default);
        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result.Result);
        Assert.Equal(429, response.StatusCode);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(0, json.GetProperty("remainingCapacity").GetInt32());
    }
    private sealed class ExhaustedSender : IManualEmailService
    {
        public Task<Lead?> SendAsync(Guid id, SendManualEmailRequest request, CancellationToken token) =>
            throw new ManualEmailException("Daily limit reached", 429, 0);
    }

    [SafetyDatabaseFact]
    public async Task MigrationAlsoSupportsEmptySchema()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SIGNALMINER_TEST_DB"));
        var schema = "safety_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await command.ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            await using var db = new SignalMinerDbContext(new DbContextOptionsBuilder<SignalMinerDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await db.Database.MigrateAsync();
            Assert.Equal(0, await db.Leads.CountAsync());
            Assert.Equal(0, await db.EmailSuppressions.CountAsync());
            Assert.Equal(0, await db.EmailSubmissions.CountAsync());
        }
        finally
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(schema,@"^safety_test_[a-f0-9]{32}$")) throw new InvalidOperationException();
            await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin); await command.ExecuteNonQueryAsync();
        }
    }
    [Theory]
    [InlineData("  Alice@EXAMPLE.com  ", "alice@example.com")]
    [InlineData("a+b@example.com", "a+b@example.com")]
    public void Normalization(string email, string expected) => Assert.Equal(expected, EmailAddress.Normalize(email));

    [Theory]
    [InlineData("invalid")]
    [InlineData("x@example.com\r\nBcc: a@example.com")]
    public void InvalidAddressesFail(string email) => Assert.Throws<ManualEmailException>(() => EmailAddress.Normalize(email));

    [Theory]
    [InlineData(0,10)] [InlineData(50,0)] [InlineData(-1,10)] [InlineData(50,-1)]
    public void InvalidPolicyFailsClosed(int limit,int delay) => Assert.Throws<InvalidOperationException>(() => new OutreachPolicy(limit,delay).Validate());

    [SafetyDatabaseFact]
    public async Task MigrationSuppressionImportAndAtomicSendingPreserveData()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SIGNALMINER_TEST_DB"));
        var schema = "safety_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}",admin)) await create.ExecuteNonQueryAsync();
        builder.SearchPath = schema;
        SignalMinerDbContext Context() => new(new DbContextOptionsBuilder<SignalMinerDbContext>().UseNpgsql(builder.ConnectionString).Options);
        try
        {
            await using var db = Context();
            // Reproduce the old EnsureCreated schema before applying the additive migration.
            var sql = db.Database.GenerateCreateScript();
            var oldStatements = System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE (?:TABLE|INDEX)[\s\S]*?;")
                .Select(m => m.Value).Where(s => !s.Contains("EmailSubmissions") && !s.Contains("EmailSuppressions"));
            await db.Database.ExecuteSqlRawAsync(string.Join("\n",oldStatements));
            var existing = new Lead { DisplayName="Existing", PublicEmail="existing@example.com", IsImported=true };
            existing.OutreachEvents.Add(new OutreachEvent { Type=OutreachEventType.ManualEmailSent, Body="Prior history", OccurredAt=DateTimeOffset.UtcNow.AddDays(-1) });
            db.Leads.Add(existing); await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
            Assert.True(await db.Leads.AnyAsync(x=>x.Id==existing.Id && x.IsImported));
            Assert.Equal(1,await db.OutreachEvents.CountAsync()); Assert.Equal(1,await db.EmailSubmissions.CountAsync());

            var suppressions = new SuppressionService(db);
            foreach (var reason in Enum.GetValues<SuppressionReason>())
            {
                var lead = new Lead { DisplayName=reason.ToString(), PublicEmail=$"  {reason}@EXAMPLE.com " };
                db.Leads.Add(lead); await db.SaveChangesAsync();
                var record = await suppressions.RecordAsync(lead.Id,new(reason,"Manual provider report"),default);
                Assert.Equal(reason,record!.Reason); Assert.Equal(ContactStatus.DoNotContact,lead.ContactStatus);
                var timestamp=record.CreatedAt;
                var repeated=await suppressions.RecordAsync(lead.Id,new(reason),default);
                Assert.Equal(timestamp,repeated!.CreatedAt);
                Assert.Single(await db.OutreachEvents.Where(x=>x.LeadId==lead.Id).ToListAsync());
                Assert.Single(await suppressions.FindAsync([lead.PublicEmail],default));
                var delivery=new FakeDelivery();
                var sender=new ManualEmailService(new LeadRepository(db),delivery,new OutreachSafetyService(db,new(50,1)));
                await Assert.ThrowsAsync<ManualEmailException>(()=>sender.SendAsync(lead.Id,Request(lead.PublicEmail.Trim()),default));
                Assert.Equal(0,delivery.Calls);
            }
            var duplicate=new Lead { DisplayName="Alias", PublicEmail=" HARDBOUNCE@example.com " };
            db.Leads.Add(duplicate);await db.SaveChangesAsync();
            var suppressedDelivery=new FakeDelivery();
            await Assert.ThrowsAsync<ManualEmailException>(()=>new ManualEmailService(new LeadRepository(db),suppressedDelivery,new OutreachSafetyService(db,new(50,1)))
                .SendAsync(duplicate.Id,Request("hardbounce@example.com"),default));
            Assert.Equal(0,suppressedDelivery.Calls);
            using var csv=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Display Name,Professional Email\nBlocked,HardBounce@example.com"));
            var imported=await new LeadImportService(new LeadRepository(db),suppressions).ImportAsync(csv,"test.csv",true,default);
            Assert.Equal(0,imported.ImportedRows);Assert.Equal(1,imported.SkippedRows);Assert.Contains(imported.Issues,x=>x.Message.Contains("Suppressed"));

            var deliverySuccess=new FakeDelivery();
            var safety=new OutreachSafetyService(db,new(1,1));
            var manual=new ManualEmailService(new LeadRepository(db),deliverySuccess,safety);
            await manual.SendAsync(existing.Id,Request(existing.PublicEmail!) with {IsTest=true,Cc="hardbounce@example.com"},default);
            Assert.Equal(1,(await safety.CapacityAsync(default)).RemainingCapacity);
            Assert.Empty(deliverySuccess.Last!.Cc);
            await Assert.ThrowsAsync<ManualEmailException>(()=>manual.SendAsync(existing.Id,Request(existing.PublicEmail!) with {Body="Hello {{firstName}}"},default));
            var attempts=Enumerable.Range(0,4).Select(async _=> {
                await using var concurrent=Context();
                var service=new ManualEmailService(new LeadRepository(concurrent),new FakeDelivery(),new OutreachSafetyService(concurrent,new(1,1)));
                try { await service.SendAsync(existing.Id,Request(existing.PublicEmail!),default);return 200; }
                catch(ManualEmailException ex) { if(ex.StatusCode==429)Assert.Equal(0,ex.RemainingCapacity);return ex.StatusCode; }
            });
            var outcomes=await Task.WhenAll(attempts);
            Assert.Equal(1,outcomes.Count(x=>x==200));Assert.Equal(3,outcomes.Count(x=>x==429));
            Assert.Equal(0,(await safety.CapacityAsync(default)).RemainingCapacity);
            Assert.Equal(2,await db.OutreachEvents.CountAsync(x=>x.Type==OutreachEventType.ManualEmailSent));

            var failureLead=new Lead {DisplayName="Failure",PublicEmail="failure@example.com"};db.Leads.Add(failureLead);await db.SaveChangesAsync();
            var failed=new ManualEmailService(new LeadRepository(db),new FakeDelivery(true),new OutreachSafetyService(db,new(2,1)));
            await Assert.ThrowsAsync<ManualEmailException>(()=>failed.SendAsync(failureLead.Id,Request(failureLead.PublicEmail),default));
            Assert.Equal(ContactStatus.NotContacted,failureLead.ContactStatus);
            Assert.True(await db.EmailSubmissions.AnyAsync(x=>x.LeadId==failureLead.Id&&x.Status==EmailSubmissionStatus.Uncertain));
            Assert.True(await db.OutreachEvents.AnyAsync(x=>x.LeadId==failureLead.Id&&x.Type==OutreachEventType.Note));
        }
        finally
        {
            // Only the randomly named test schema is removed; application tables are never targeted.
            if (!System.Text.RegularExpressions.Regex.IsMatch(schema,@"^safety_test_[a-f0-9]{32}$"))throw new InvalidOperationException();
            await using var drop=new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE",admin);await drop.ExecuteNonQueryAsync();
        }
    }
    private static SendManualEmailRequest Request(string email)=>new(email,"Hello","Hello friend",null,null,null,null,[]);
    private sealed class FakeDelivery(bool fail=false):IEmailDeliveryService
    {
        public int Calls;public EmailMessage? Last;
        public Task<EmailDeliveryResult> SendAsync(EmailMessage message,CancellationToken token)
        { Calls++;Last=message;if(fail)throw new ManualEmailException("Simulated SMTP failure",502);return Task.FromResult(new EmailDeliveryResult("Submitted","test-only",DateTimeOffset.UtcNow)); }
    }
}
