# Niobium.Invoicing.Abstractions

Niobium.Invoicing.Abstractions defines the core contracts, types, and shared logic for the Niobium Invoicing platform. It provides interfaces and base types for invoice management, billing, and integration with Cod.* packages.

## What is this project about?
- Defines interfaces and base types for invoice, billing, and platform services.
- Used as a foundational dependency by Niobium.Invoicing.Core, Niobium.Invoicing.Portal, and related projects.
- Ensures a consistent contract for invoicing and billing logic across the solution.

## Getting Started

### 1. Install the NuGet Package
Add the package to your .NET project:

```
dotnet add package Niobium.Invoicing.Abstractions
```

### 2. Use the Abstractions in Your Code
Implement the interfaces and use the base types in your domain and infrastructure code:

```csharp
using Niobium.Invoicing;

public class MyInvoicingService : IInvoicingPlatformService
{
    public Task IssueInvoiceAsync(Invoice invoice, IEnumerable<InvoiceItem> invoiceItems, CancellationToken cancellationToken = default)
    {
        // Implementation here
    }
}
```

### 3. Integrate with Other Niobium and Cod Packages
Niobium.Invoicing.Abstractions is referenced by Niobium.Invoicing.Core, Niobium.Invoicing.Portal, and other projects. Just add the relevant projects or packages to your solution and use the shared contracts.

## Contributing

Contributions are welcome! To contribute:
1. Fork the repository
2. Create a feature branch
3. Make your changes with clear commit messages
4. Submit a pull request

Please ensure your code follows the existing style and includes appropriate tests and documentation.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
