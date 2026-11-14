using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Niobium.Invoicing.Core.Tests")]
[assembly: InternalsVisibleTo("Niobium.Invoicing.Integration.Tests")]

// Preserve the original Core type identities for consumers of the history writer.
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceChangeKind))]
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceChangeAuthority))]
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceLineEvidence))]
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceChange))]
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceChangeOrigin))]
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceRevisionLifecycle))]
[assembly: TypeForwardedTo(typeof(Niobium.Invoicing.History.InvoiceRevisionSnapshot))]
