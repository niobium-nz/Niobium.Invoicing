using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Niobium.Invoicing.History;

/// <summary>A short-lived continuation for a previously verified committed chain.</summary>
internal static class InvoiceHistoryCursor
{
    private const int PayloadLength = 25;
    private const int EncodedLength = 76;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("Niobium.Invoicing.InvoiceHistoryCursor/v1\0");

    internal static string Create(Invoice invoice, Guid revision, DateTimeOffset now, string secret)
    {
        ValidateContext(invoice, secret);
        if (revision == Guid.Empty)
            throw new ArgumentException("A history revision is required.", nameof(revision));
        byte[] value = new byte[PayloadLength + 32];
        value[0] = 1;
        revision.TryWriteBytes(value.AsSpan(1, 16));
        BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(17, 8), now.AddMinutes(30).ToUnixTimeSeconds());
        Sign(invoice, secret, value.AsSpan(0, PayloadLength)).CopyTo(value, PayloadLength);
        return Encode(value);
    }

    internal static bool TryResolve(string cursor, Invoice invoice, DateTimeOffset now, string secret, out Guid revision)
    {
        revision = Guid.Empty;
        ValidateContext(invoice, secret);
        if (cursor.Length != EncodedLength || cursor.Any(c => !(Char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return false;
        byte[] value;
        try
        {
            value = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException) { return false; }
        if (value.Length != PayloadLength + 32 || value[0] != 1 || Encode(value) != cursor)
            return false;
        byte[] signature = Sign(invoice, secret, value.AsSpan(0, PayloadLength));
        if (!CryptographicOperations.FixedTimeEquals(signature, value.AsSpan(PayloadLength)))
            return false;
        long expires = BinaryPrimitives.ReadInt64BigEndian(value.AsSpan(17, 8));
        if (expires <= now.ToUnixTimeSeconds())
            return false;
        revision = new Guid(value.AsSpan(1, 16));
        return revision != Guid.Empty;
    }

    private static byte[] Sign(Invoice invoice, string secret, ReadOnlySpan<byte> payload)
    {
        byte[] message = new byte[Purpose.Length + 40 + PayloadLength];
        Purpose.CopyTo(message, 0);
        invoice.Biller.TryWriteBytes(message.AsSpan(Purpose.Length, 16));
        invoice.Tenant.TryWriteBytes(message.AsSpan(Purpose.Length + 16, 16));
        BinaryPrimitives.WriteInt64BigEndian(message.AsSpan(Purpose.Length + 32, 8), invoice.GetID());
        payload.CopyTo(message.AsSpan(Purpose.Length + 40));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message);
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_');

    private static void ValidateContext(Invoice invoice, string secret)
    {
        if (invoice.Biller == Guid.Empty || invoice.Tenant == Guid.Empty || invoice.GetID() <= 0 || String.IsNullOrWhiteSpace(secret))
            throw new ApplicationException(InternalError.InternalServerError, "Invoice history continuation configuration is unavailable.");
    }
}
