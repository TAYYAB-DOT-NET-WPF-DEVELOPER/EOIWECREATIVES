using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WeCreatives_EOI.Classes
{
    public class CallCenterOrderRoot
    {
        [JsonPropertyName("id")]
        public object? id { get; set; }

        [JsonPropertyName("branch_id")]
        public object? branch_id { get; set; }

        [JsonPropertyName("customer_id")]
        public object? customer_id { get; set; }

        [JsonPropertyName("customer_address_id")]
        public object? customer_address_id { get; set; }

        [JsonPropertyName("order_number")]
        public string? order_number { get; set; }

        [JsonPropertyName("source")]
        public string? source { get; set; }

        [JsonPropertyName("orderType")]
        public string? orderType { get; set; }

        [JsonPropertyName("store_code")]
        public string? store_code { get; set; }

        [JsonPropertyName("sale_type_index")]
        public object? sale_type_index { get; set; }

        [JsonPropertyName("payment_method_index")]
        public object? payment_method_index { get; set; }

        [JsonPropertyName("order_status")]
        public string? order_status { get; set; }

        [JsonPropertyName("sub_total")]
        public decimal sub_total { get; set; }

        [JsonPropertyName("tax_amount")]
        public decimal tax_amount { get; set; }

        [JsonPropertyName("delivery_charges")]
        public decimal delivery_charges { get; set; }

        [JsonPropertyName("discount_amount")]
        public decimal discount_amount { get; set; }

        [JsonPropertyName("total_amount")]
        public decimal total_amount { get; set; }

        [JsonPropertyName("notes")]
        public string? notes { get; set; }

        [JsonPropertyName("placed_by_pos_user_id")]
        public object? placed_by_pos_user_id { get; set; }

        [JsonPropertyName("approval_no")]
        public string? approval_no { get; set; }

        [JsonPropertyName("card_number")]
        public string? card_number { get; set; }

        [JsonPropertyName("user_def1")]
        public string? user_def1 { get; set; }

        [JsonPropertyName("user_def2")]
        public string? user_def2 { get; set; }

        [JsonPropertyName("future_date_time")]
        public string? future_date_time { get; set; }

        [JsonPropertyName("customer_info")]
        public CallCenterCustomerInfo? customer_info { get; set; }

        [JsonPropertyName("customer_address")]
        public CallCenterCustomerAddress? customer_address { get; set; }

        [JsonPropertyName("order_mode")]
        public string? order_mode { get; set; }

        [JsonPropertyName("items")]
        public List<CallCenterOrderItem>? items { get; set; }
    }

    public class CallCenterCustomerInfo
    {
        [JsonPropertyName("id")]
        public object? id { get; set; }

        [JsonPropertyName("first_name")]
        public string? first_name { get; set; }

        [JsonPropertyName("last_name")]
        public string? last_name { get; set; }

        [JsonPropertyName("phone")]
        public string? phone { get; set; }

        [JsonPropertyName("email")]
        public string? email { get; set; }
    }

    public class CallCenterCustomerAddress
    {
        [JsonPropertyName("id")]
        public object? id { get; set; }

        [JsonPropertyName("customer_id")]
        public object? customer_id { get; set; }

        [JsonPropertyName("sub_region_id")]
        public object? sub_region_id { get; set; }

        [JsonPropertyName("address_type")]
        public string? address_type { get; set; }

        [JsonPropertyName("address_line1")]
        public string? address_line1 { get; set; }

        [JsonPropertyName("address_line2")]
        public string? address_line2 { get; set; }

        [JsonPropertyName("address_line3")]
        public string? address_line3 { get; set; }

        [JsonPropertyName("city")]
        public string? city { get; set; }

        [JsonPropertyName("nearby_landmark")]
        public string? nearby_landmark { get; set; }

        [JsonPropertyName("province")]
        public string? province { get; set; }

        [JsonPropertyName("zip")]
        public string? zip { get; set; }

        [JsonPropertyName("tax_id")]
        public bool tax_id { get; set; }
    }

    public class CallCenterOrderItem
    {
        [JsonPropertyName("id")]
        public object? id { get; set; }

        [JsonPropertyName("order_id")]
        public object? order_id { get; set; }

        [JsonPropertyName("product_id")]
        public object? product_id { get; set; }

        [JsonPropertyName("deal_id")]
        public object? deal_id { get; set; }

        [JsonPropertyName("item_name")]
        public string? item_name { get; set; }

        [JsonPropertyName("quantity")]
        public decimal quantity { get; set; }

        [JsonPropertyName("unit_price")]
        public decimal unit_price { get; set; }

        [JsonPropertyName("sub_total")]
        public decimal sub_total { get; set; }

        [JsonPropertyName("extra_price")]
        public decimal extra_price { get; set; }

        [JsonPropertyName("total_price")]
        public decimal total_price { get; set; }

        [JsonPropertyName("is_topping_row")]
        public bool is_topping_row { get; set; }

        [JsonPropertyName("ref_code")]
        public string? ref_code { get; set; }

        [JsonPropertyName("pos_code")]
        public string? pos_code { get; set; }

        [JsonPropertyName("child_items")]
        public List<CallCenterOrderItem>? child_items { get; set; }

        [JsonPropertyName("modifiers")]
        public List<CallCenterModifier>? modifiers { get; set; }
    }

    public class CallCenterModifier
    {
        [JsonPropertyName("id")]
        public object? id { get; set; }

        [JsonPropertyName("order_item_id")]
        public object? order_item_id { get; set; }

        [JsonPropertyName("variant_option_item_id")]
        public object? variant_option_item_id { get; set; }

        [JsonPropertyName("variant_option_item_option_id")]
        public object? variant_option_item_option_id { get; set; }

        [JsonPropertyName("modifier_name")]
        public string? modifier_name { get; set; }

        [JsonPropertyName("unit_price")]
        public decimal unit_price { get; set; }

        [JsonPropertyName("quantity")]
        public decimal quantity { get; set; }

        [JsonPropertyName("total_price")]
        public decimal total_price { get; set; }

        [JsonPropertyName("ref_code")]
        public string? ref_code { get; set; }

        [JsonPropertyName("pos_code")]
        public string? pos_code { get; set; }

        [JsonPropertyName("child_items")]
        public List<CallCenterOrderItem>? child_items { get; set; }

        [JsonPropertyName("modifiers")]
        public List<CallCenterModifier>? modifiers { get; set; }
    }
}
