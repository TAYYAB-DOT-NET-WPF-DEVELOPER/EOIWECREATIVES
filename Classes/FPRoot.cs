using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeCreatives_EOI.Classes
{
    public class FPRoot
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("shortCode")]
        public string? ShortCode { get; set; }

        [JsonPropertyName("preOrder")]
        public bool PreOrder { get; set; }

        [JsonPropertyName("expiryDate")]
        public DateTime? ExpiryDate { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("platformRestaurant")]
        public FPPlatformRestaurant PlatformRestaurant { get; set; } = new();

        [JsonPropertyName("customer")]
        public FPCustomer Customer { get; set; } = new();

        [JsonPropertyName("payment")]
        public FPPayment Payment { get; set; } = new();

        [JsonPropertyName("expeditionType")]
        public string? ExpeditionType { get; set; }

        [JsonPropertyName("products")]
        public List<FPProduct> Products { get; set; } = new();

        [JsonPropertyName("comments")]
        public FPComments Comments { get; set; } = new();

        [JsonPropertyName("discounts")]
        public List<FPDiscount> Discounts { get; set; } = new();

        [JsonPropertyName("price")]
        public FPPrice Price { get; set; } = new();

        [JsonPropertyName("delivery")]
        public FPDelivery Delivery { get; set; } = new();

        [JsonPropertyName("vouchers")]
        public List<JsonElement> Vouchers { get; set; } = new();

        [JsonPropertyName("webOrder")]
        public bool WebOrder { get; set; }

        [JsonPropertyName("mobileOrder")]
        public bool MobileOrder { get; set; }

        [JsonPropertyName("corporateOrder")]
        public bool CorporateOrder { get; set; }

        [JsonPropertyName("callbackUrls")]
        public FPCallbackUrls CallbackUrls { get; set; } = new();
    }

    public class FPCallbackUrls
    {
        [JsonPropertyName("orderAcceptedUrl")]
        public string? OrderAcceptedUrl { get; set; }

        [JsonPropertyName("orderRejectedUrl")]
        public string? OrderRejectedUrl { get; set; }

        [JsonPropertyName("orderPickedUpUrl")]
        public string? OrderPickedUpUrl { get; set; }

        [JsonPropertyName("orderPreparedUrl")]
        public string? OrderPreparedUrl { get; set; }

        [JsonPropertyName("orderProductModificationUrl")]
        public string? OrderProductModificationUrl { get; set; }
    }

    public class FPPlatformRestaurant
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    public class FPCustomer
    {
        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("mobilePhone")]
        public string? MobilePhone { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("flags")]
        public List<string> Flags { get; set; } = new();

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("mobilePhoneCountryCode")]
        public string? MobilePhoneCountryCode { get; set; }
    }

    public class FPPayment
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("remoteCode")]
        public string? RemoteCode { get; set; }

        [JsonPropertyName("requiredMoneyChange")]
        public string? RequiredMoneyChange { get; set; }

        [JsonPropertyName("vatName")]
        public string? VatName { get; set; }

        [JsonPropertyName("vatId")]
        public string? VatId { get; set; }
    }

    public class FPProduct
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("remoteCode")]
        public string? RemoteCode { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("categoryName")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("unitPrice")]
        public string? UnitPrice { get; set; }

        [JsonPropertyName("paidPrice")]
        public string? PaidPrice { get; set; }

        [JsonPropertyName("quantity")]
        public string? Quantity { get; set; }

        [JsonPropertyName("selectedToppings")]
        public List<JsonElement> SelectedToppings { get; set; } = new();

        [JsonPropertyName("selectedChoices")]
        public List<JsonElement> SelectedChoices { get; set; } = new();

        [JsonPropertyName("discounts")]
        public List<JsonElement> Discounts { get; set; } = new();

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("discountAmount")]
        public string? DiscountAmount { get; set; }

        [JsonPropertyName("vatPercentage")]
        public string? VatPercentage { get; set; }
    }

    public class FPComments
    {
        [JsonPropertyName("customerComment")]
        public string? CustomerComment { get; set; }

        [JsonPropertyName("vendorComment")]
        public string? VendorComment { get; set; }
    }

    public class FPDiscount
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("price_off")]
        public string? PriceOff { get; set; }

        [JsonPropertyName("ref")]
        public string? Ref { get; set; }
    }

    public class FPPrice
    {
        [JsonPropertyName("minimumDeliveryValue")]
        public string? MinimumDeliveryValue { get; set; }

        [JsonPropertyName("comission")]
        public string? Comission { get; set; }

        [JsonPropertyName("deliveryFee")]
        public string? DeliveryFee { get; set; }

        [JsonPropertyName("containerCharge")]
        public string? ContainerCharge { get; set; }

        [JsonPropertyName("subTotal")]
        public string? SubTotal { get; set; }

        [JsonPropertyName("totalNet")]
        public string? TotalNet { get; set; }

        [JsonPropertyName("vatPercent")]
        public string? VatPercent { get; set; }

        [JsonPropertyName("vatTotal")]
        public string? VatTotal { get; set; }

        [JsonPropertyName("grandTotal")]
        public string? GrandTotal { get; set; }

        [JsonPropertyName("discountAmountTotal")]
        public string? DiscountAmountTotal { get; set; }

        [JsonPropertyName("payRestaurant")]
        public string? PayRestaurant { get; set; }

        [JsonPropertyName("riderTip")]
        public string? RiderTip { get; set; }
    }

    public class FPDelivery
    {
        [JsonPropertyName("expressDelivery")]
        public bool ExpressDelivery { get; set; }

        [JsonPropertyName("expectedDeliveryTime")]
        public string? ExpectedDeliveryTime { get; set; }

        [JsonPropertyName("riderPickupTime")]
        public string? RiderPickupTime { get; set; }

        [JsonPropertyName("address")]
        public FPAddress? Address { get; set; }
    }

    public class FPAddress
    {
        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("number")]
        public string? Number { get; set; }

        [JsonPropertyName("district")]
        public string? District { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("building")]
        public string? Building { get; set; }

        [JsonPropertyName("floor")]
        public string? Floor { get; set; }

        [JsonPropertyName("intercom")]
        public string? Intercom { get; set; }
    }
}
