namespace Stockly.Application.Features.Billing;

/// <summary>Limits of the billing API (contract §12). The only place they are defined.</summary>
public static class BillingLimits
{
    /// <summary>B3 <c>walletPhone</c>: an Egyptian mobile number (Vodafone, Etisalat, Orange, WE).</summary>
    public const string WalletPhonePattern = "^01[0125][0-9]{8}$";
}
