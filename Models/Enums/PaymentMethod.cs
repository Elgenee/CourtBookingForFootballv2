namespace CourtBookingSystem.Models.Enums
{
    public enum PaymentMethod
    {
        Cash = 1,
        GCash = 2,
        // PayMongo QR Ph — Philippines instant QR payment processed through
        // PayMongo Checkout Sessions. See Documentation/PAYMONGO.md.
        PayMongoQrPh = 3
    }
}
