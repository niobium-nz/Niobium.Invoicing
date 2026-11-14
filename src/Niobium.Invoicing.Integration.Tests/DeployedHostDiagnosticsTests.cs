using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class DeployedHostDiagnosticsTests
{
    [TestMethod]
    public void Only_fixed_labels_and_nonnegative_counts_can_enter_the_report()
    {
        var rows = DeployedHostDiagnostics.Parse("suite\tcurrent\t1\t2\t3\t4\t5\t6\t7\t8\t9\t10\n");
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(2L, rows[0].OutOfMemory);
        Assert.AreEqual(9L, rows[0].Requests);
        Assert.AreEqual(0, DeployedHostDiagnostics.Parse("").Count);
    }

    [TestMethod]
    [DataRow("suite\tcurrent\tprivate-message")]
    [DataRow("private-value\tcurrent\t1\t2\t3\t4\t5\t6\t7\t8\t9\t10")]
    [DataRow("suite\tprivate-value\t1\t2\t3\t4\t5\t6\t7\t8\t9\t10")]
    [DataRow("suite\tcurrent\t-1\t2\t3\t4\t5\t6\t7\t8\t9\t10")]
    [DataRow("suite\tcurrent\tsecret\t2\t3\t4\t5\t6\t7\t8\t9\t10")]
    [DataRow("suite\tcurrent\t9223372036854775808\t2\t3\t4\t5\t6\t7\t8\t9\t10")]
    public void Unexpected_data_is_rejected_without_echoing_it(string response)
    {
        FormatException error = Assert.ThrowsExactly<FormatException>(() => DeployedHostDiagnostics.Parse(response));
        Assert.IsFalse(error.Message.Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Duplicate_and_unbounded_groups_are_rejected()
    {
        const string row = "suite\tcurrent\t1\t2\t3\t4\t5\t6\t7\t8\t9\t10\n";
        Assert.ThrowsExactly<FormatException>(() => DeployedHostDiagnostics.Parse(row + row));
        Assert.ThrowsExactly<FormatException>(() => DeployedHostDiagnostics.Parse(new string('x', 4097)));
    }

    [TestMethod]
    public void Query_rejects_injection_and_unbounded_windows()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        Assert.ThrowsExactly<ArgumentException>(() => DeployedHostDiagnostics.Query(started, started.AddMinutes(1), "1.2.3\" | take 1"));
        Assert.ThrowsExactly<ArgumentException>(() => DeployedHostDiagnostics.Query(started, started.AddMinutes(-1), "1.2.3"));
        Assert.ThrowsExactly<ArgumentException>(() => DeployedHostDiagnostics.Query(started, started.AddHours(2), "1.2.3"));
        string query = DeployedHostDiagnostics.Query(started, started.AddMinutes(1), "1.2.3");
        StringAssert.Contains(query, "1.2.3-prerelease");
        StringAssert.Contains(query, "| summarize");
    }
}
