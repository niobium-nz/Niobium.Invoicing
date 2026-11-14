using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.History;
using Niobium.Platform;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    [DataRow(nameof(InvoiceRevision))]
    [DataRow(nameof(InvoiceRevisionPart))]
    public async Task History_models_do_not_gain_a_direct_client_table_grant(string table)
    {
        Guid biller = Guid.NewGuid();
        ClaimsPrincipal principal = ResourcePrincipal(biller);
        foreach (IResourceControl control in host!.Services.GetServices<IResourceControl>())
        {
            bool applicable = control.Grantable(ResourceType.AzureStorageTable, table);
            if (!applicable)
                continue;
            StorageControl? grant = await control.GrantAsync(principal, ResourceType.AzureStorageTable,
                table, biller.ToString(), null);
            Assert.IsNull(grant, "History registration must not grant a client direct access to audit rows.");
        }
    }

    [TestMethod]
    public async Task Registered_item_control_grants_only_query_for_the_owned_invoice_partition()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<UpsertFlow>().RunAsync(request, null, CancellationToken.None);
        string partition = InvoiceItem.BuildPartitionKey(request.InvoiceID);
        ClaimsPrincipal principal = ResourcePrincipal(request.BillerID);
        IResourceControl[] controls = ItemControls();
        Assert.IsNotEmpty(controls, "The real Server registration must include an item resource control.");

        List<StorageControl> grants = [];
        foreach (IResourceControl control in controls)
        {
            StorageControl? grant = await control.GrantAsync(principal, ResourceType.AzureStorageTable,
                nameof(InvoiceItem), partition, null);
            if (grant != null)
                grants.Add(grant);
        }

        Assert.HasCount(1, grants);
        Assert.AreEqual(nameof(InvoiceItem), grants[0].Resource);
        Assert.AreEqual((int)DatabasePermissions.Query, grants[0].Permission);
        Assert.AreEqual(partition, grants[0].StartPartitionKey);
        Assert.AreEqual(partition, grants[0].EndPartitionKey);
    }

    [TestMethod]
    [DataRow("other-biller")]
    [DataRow("no-partition")]
    [DataRow("missing-invoice")]
    public async Task Registered_item_controls_reject_unowned_or_unbounded_requests(string mode)
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<UpsertFlow>().RunAsync(request, null, CancellationToken.None);
        ClaimsPrincipal principal = ResourcePrincipal(mode == "other-biller" ? Guid.NewGuid() : request.BillerID);
        string? partition = mode == "no-partition" ? null : mode == "missing-invoice"
            ? "missing-" + Guid.NewGuid().ToString("N") : InvoiceItem.BuildPartitionKey(request.InvoiceID);
        IResourceControl[] controls = ItemControls();
        Assert.IsNotEmpty(controls);
        foreach (IResourceControl control in controls)
        {
            StorageControl? grant = await control.GrantAsync(principal, ResourceType.AzureStorageTable,
                nameof(InvoiceItem), partition, null);
            Assert.IsNull(grant, "No registered control may grant item access to this synthetic principal.");
        }
    }

    private static ClaimsPrincipal ResourcePrincipal(Guid user) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Sid, user.ToString())], "SyntheticIntegration"));

    private static IResourceControl[] ItemControls() => host!.Services.GetServices<IResourceControl>()
        .Where(control => control.Grantable(ResourceType.AzureStorageTable, nameof(InvoiceItem))).ToArray();
}
