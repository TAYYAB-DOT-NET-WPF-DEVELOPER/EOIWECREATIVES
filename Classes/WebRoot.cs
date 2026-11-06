using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeCreatives_EOI.Classes
{
    public class WebRoot
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("platformRestaurant")]
        public WebPlatformRestaurant PlatformRestaurant { get; set; } = new();

        [JsonPropertyName("customer")]
        public WebCustomer Customer { get; set; } = new();

        [JsonPropertyName("payment")]
        public WebPayment Payment { get; set; } = new();

        [JsonPropertyName("expeditionType")]
        public WebExpeditionType ExpeditionType { get; set; } = new();

        [JsonPropertyName("products")]
        public List<WebProduct> Products { get; set; } = new();

        [JsonPropertyName("comments")]
        public WebComments Comments { get; set; } = new();

        [JsonPropertyName("discounts")]
        public List<JsonElement> Discounts { get; set; } = new();

        [JsonPropertyName("price")]
        public WebPrice Price { get; set; } = new();

        [JsonPropertyName("delivery")]
        public WebDelivery Delivery { get; set; } = new();

        [JsonPropertyName("vouchers")]
        public List<JsonElement> Vouchers { get; set; } = new();

        [JsonPropertyName("webOrder")]
        public bool WebOrder { get; set; }

        [JsonPropertyName("mobileOrder")]
        public bool MobileOrder { get; set; }

        [JsonPropertyName("callCenter")]
        public bool CallCenter { get; set; }

        [JsonPropertyName("corporateOrder")]
        public bool CorporateOrder { get; set; }

        [JsonPropertyName("integrationInfo")]
        public Dictionary<string, JsonElement> IntegrationInfo { get; set; } = new();

        [JsonPropertyName("loyalty")]
        public WebLoyalty Loyalty { get; set; } = new();
    }

    public class WebPlatformRestaurant
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    public class WebCustomer
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

    public class WebPayment
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("remoteCode")]
        [JsonConverter(typeof(SystemTextJsonStringConverter))]
        public string? RemoteCode { get; set; }
    }

    public class WebProduct
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

        [JsonPropertyName("variation")]
        public WebVariation Variation { get; set; } = new();

        [JsonPropertyName("unitPrice")]
        public string? UnitPrice { get; set; }

        [JsonPropertyName("paidPrice")]
        public string? PaidPrice { get; set; }

        [JsonPropertyName("quantity")]
        public string? Quantity { get; set; }

        [JsonPropertyName("selectedToppings")]
        public List<JsonElement> SelectedToppings { get; set; } = new();

        [JsonPropertyName("discounts")]
        public List<JsonElement> Discounts { get; set; } = new();

        [JsonPropertyName("vatPercentage")]
        public string? VatPercentage { get; set; }

        [JsonPropertyName("selectedChoices")]
        public List<JsonElement> SelectedChoices { get; set; } = new();
    }

    public class WebVariation
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class WebComments
    {
        [JsonPropertyName("customerComment")]
        public string? CustomerComment { get; set; }

        [JsonPropertyName("vendorComment")]
        public string? VendorComment { get; set; }
    }

    public class WebPrice
    {
        [JsonPropertyName("minimumDeliveryValue")]
        public string? MinimumDeliveryValue { get; set; }

        // JSON field spelling is "comission"
        [JsonPropertyName("comission")]
        public string? Commission { get; set; }

        [JsonPropertyName("deliveryFee")]
        public string? DeliveryFee { get; set; }

        [JsonPropertyName("deliveryFees")]
        public List<JsonElement> DeliveryFees { get; set; } = new();

        [JsonPropertyName("containerCharge")]
        public string? ContainerCharge { get; set; }

        [JsonPropertyName("deliveryFeeDiscount")]
        public string? DeliveryFeeDiscount { get; set; }

        [JsonPropertyName("serviceFeePercent")]
        public string? ServiceFeePercent { get; set; }

        [JsonPropertyName("serviceFeeTotal")]
        public string? ServiceFeeTotal { get; set; }

        [JsonPropertyName("serviceTax")]
        public decimal ServiceTax { get; set; }

        [JsonPropertyName("serviceTaxValue")]
        public decimal ServiceTaxValue { get; set; }

        [JsonPropertyName("subTotal")]
        public string? SubTotal { get; set; }

        [JsonPropertyName("totalNet")]
        public string? TotalNet { get; set; }

        [JsonPropertyName("vatVisible")]
        public bool VatVisible { get; set; }

        [JsonPropertyName("vatPercent")]
        public string? VatPercent { get; set; }

        [JsonPropertyName("vatTotal")]
        public string? VatTotal { get; set; }

        [JsonPropertyName("grandTotal")]
        public string? GrandTotal { get; set; }

        [JsonPropertyName("discountAmountTotal")]
        public string? DiscountAmountTotal { get; set; }

        [JsonPropertyName("collectFromCustomer")]
        public string? CollectFromCustomer { get; set; }

        [JsonPropertyName("riderTip")]
        public string? RiderTip { get; set; }
    }

    public class WebDelivery
    {
        [JsonPropertyName("expressDelivery")]
        public bool ExpressDelivery { get; set; }

        [JsonPropertyName("express_delivery")]
        public bool ExpressDeliverySnake { set => ExpressDelivery = value; }

        [JsonPropertyName("expectedDeliveryTime")]
        public string? ExpectedDeliveryTime { get; set; }

        [JsonPropertyName("expected_delivery_time")]
        public string? ExpectedDeliveryTimeSnake { set => ExpectedDeliveryTime = value; }

        [JsonPropertyName("riderPickupTime")]
        public string? RiderPickupTime { get; set; }

        [JsonPropertyName("rider_pickup_time")]
        public string? RiderPickupTimeSnake { set => RiderPickupTime = value; }

        [JsonPropertyName("address")]
        public WebAddress? Address { get; set; }
    }

    [JsonConverter(typeof(WebAddressJsonConverter))]
    public class WebAddress
    {
        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("number")]
        public string? Number { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }

        [JsonPropertyName("district")]
        public string? District { get; set; }

        [JsonPropertyName("company")]
        public string? Company { get; set; }

        [JsonPropertyName("deliveryArea")]
        public string? DeliveryArea { get; set; }

        [JsonPropertyName("latitude")]
        public string? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public string? Longitude { get; set; }
    }

    public class WebLoyalty
    {
        [JsonPropertyName("earning")]
        public string? Earning { get; set; }

        [JsonPropertyName("redemption")]
        public string? Redemption { get; set; }
    }

    public class WebExpeditionType
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("remoteCode")]
        [JsonConverter(typeof(SystemTextJsonStringConverter))]
        public string? RemoteCode { get; set; }
    }

    public class SystemTextJsonStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
                {
                    return doc.RootElement.GetRawText();
                }
            }
            if (reader.TokenType == JsonTokenType.True)
            {
                return "true";
            }
            if (reader.TokenType == JsonTokenType.False)
            {
                return "false";
            }
            return reader.GetString() ?? string.Empty;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public class WebAddressJsonConverter : JsonConverter<WebAddress>
    {
        public override WebAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string? str = reader.GetString();
                return string.IsNullOrWhiteSpace(str) ? null : new WebAddress { Street = str };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var address = new WebAddress();
                string? currentPropName = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return address;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        currentPropName = reader.GetString();
                        continue;
                    }

                    if (!string.IsNullOrEmpty(currentPropName))
                    {
                        string val = string.Empty;
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            val = reader.GetString() ?? string.Empty;
                        }
                        else if (reader.TokenType == JsonTokenType.Number || reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                        {
                            val = System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
                        }

                        if (string.Equals(currentPropName, "street", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentPropName, "line1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentPropName, "address_line_1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentPropName, "address1", StringComparison.OrdinalIgnoreCase))
                        {
                            address.Street = val;
                        }
                        else if (string.Equals(currentPropName, "number", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "building_number", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "house_number", StringComparison.OrdinalIgnoreCase))
                        {
                            address.Number = val;
                        }
                        else if (string.Equals(currentPropName, "city", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "town", StringComparison.OrdinalIgnoreCase))
                        {
                            address.City = val;
                        }
                        else if (string.Equals(currentPropName, "postcode", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "zip", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "zip_code", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "postal_code", StringComparison.OrdinalIgnoreCase))
                        {
                            address.Postcode = val;
                        }
                        else if (string.Equals(currentPropName, "district", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "state", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "province", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "line2", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "address_line_2", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "address2", StringComparison.OrdinalIgnoreCase))
                        {
                            address.District = val;
                        }
                        else if (string.Equals(currentPropName, "company", StringComparison.OrdinalIgnoreCase))
                        {
                            address.Company = val;
                        }
                        else if (string.Equals(currentPropName, "deliveryArea", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "delivery_area", StringComparison.OrdinalIgnoreCase))
                        {
                            address.DeliveryArea = val;
                        }
                        else if (string.Equals(currentPropName, "latitude", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "lat", StringComparison.OrdinalIgnoreCase))
                        {
                            address.Latitude = val;
                        }
                        else if (string.Equals(currentPropName, "longitude", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "lng", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(currentPropName, "lon", StringComparison.OrdinalIgnoreCase))
                        {
                            address.Longitude = val;
                        }

                        currentPropName = null;
                    }
                }
                return address;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        string? s = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                }
                return list.Count > 0 ? new WebAddress { Street = string.Join(", ", list) } : null;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, WebAddress value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            if (value.Street != null) writer.WriteString("street", value.Street);
            if (value.Number != null) writer.WriteString("number", value.Number);
            if (value.City != null) writer.WriteString("city", value.City);
            if (value.Postcode != null) writer.WriteString("postcode", value.Postcode);
            if (value.District != null) writer.WriteString("district", value.District);
            if (value.Company != null) writer.WriteString("company", value.Company);
            if (value.DeliveryArea != null) writer.WriteString("deliveryArea", value.DeliveryArea);
            if (value.Latitude != null) writer.WriteString("latitude", value.Latitude);
            if (value.Longitude != null) writer.WriteString("longitude", value.Longitude);
            writer.WriteEndObject();
        }
    }
}