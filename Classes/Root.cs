using System;
using System.Collections.Generic;

namespace WeCreatives_EOI.Classes
{
    public class Root
    {
        public string? id { get; set; }
        public string? location_id { get; set; }
        public string? @ref { get; set; }
        public string? private_ref { get; set; }
        public string? status { get; set; }
        public string? service_type { get; set; }
        public string? service_type_ref { get; set; }
        public DateTime? created_at { get; set; }
        public string? created_by { get; set; }
        public string? expected_time { get; set; }
        public string? confirmed_time { get; set; }
        public string? customer_notes { get; set; }
        public string? seller_notes { get; set; }
        public string? collection_code { get; set; }
        public List<string>? coupon_codes { get; set; }
        public string? total { get; set; }
        public string? total_discrepancy { get; set; }
        public string? payment_discrepancy { get; set; }
        public List<Item>? items { get; set; }
        public Dictionary<string, Deal>? Deals { get; set; }
        public List<Discount>? discounts { get; set; }
        public List<Charge>? charges { get; set; }
        public List<Payment>? payments { get; set; }
        public Customer? customer { get; set; }
        public string? delivery { get; set; }

        //public List<string>? loyalty_operations { get; set; }
    }

    public class Discount
    {
        public string? id { get; set; }
        public string? private_ref { get; set; }
        public string? name { get; set; }
        public string? @ref { get; set; }
        public string? pricing_effect { get; set; }
        public string? pricing_value { get; set; }
        public string? price_off { get; set; }
        public string? deleted { get; set; }
    }

    public class Item
    {
        public string? id { get; set; }
        public string? private_ref { get; set; }
        public string? product_name { get; set; }
        public string? sku_name { get; set; }
        public string? sku_ref { get; set; }
        public string? price { get; set; }
        public string? quantity { get; set; }
        public string? subtotal { get; set; }
        public string? tax_rate { get; set; }
        public string? customer_notes { get; set; }
        public string? points_earned { get; set; }
        public string? points_used { get; set; }
        public List<Option>? options { get; set; }
        public bool? deleted { get; set; }
        public DealLine deal_line { get; set; }
    }

    public class Option
    {
        public string? option_list_name { get; set; }
        public string? name { get; set; }
        public string? @ref { get; set; }
        public string? price { get; set; }
        public bool? Removed { get; set; }
        public string? quantity { get; set; }
    }

    public class Charge
    {
        public string? id { get; set; }
        public string? private_ref { get; set; }
        public string? type { get; set; }
        public string? name { get; set; }
        public string? @ref { get; set; }
        public string? price { get; set; }
        public string? tax_rate { get; set; }
        public string? charge_type { get; set; }
        public string? charge_ref { get; set; }
        public string? charge_price { get; set; }
        public bool? deleted { get; set; }
    }

    public class Customer
    {
        public string? id { get; set; }
        public string? customer_list_id { get; set; }
        public bool? anonymised { get; set; }
        public string? private_ref { get; set; }
        public string? email { get; set; }
        public string? first_name { get; set; }
        public string? last_name { get; set; }
        public string? gender { get; set; }
        public string? birth_date { get; set; }
        public string? company_name { get; set; }
        public string? phone { get; set; }
        public string? phone_access_code { get; set; }
        public string? address_1 { get; set; }
        public string? address_2 { get; set; }
        public string? postal_code { get; set; }
        public string? city { get; set; }
        public string? state { get; set; }
        public string? country { get; set; }
        public string? latitude { get; set; }
        public string? longitude { get; set; }
        public string? delivery_notes { get; set; }
        public bool? sms_marketing { get; set; }
        public bool? email_marketing { get; set; }
        public int? nb_orders { get; set; }
        public string? order_total { get; set; }
        public DateTime? first_order_date { get; set; }
        public DateTime? last_order_date { get; set; }
        public CustomFields custom_fields { get; set; }

    }

    public class PixelPoint
    {
        public Dictionary<string, string> member_codes { get; set; }
    }
    public class DeliveryHero
    {
        public string? flags { get; set; }
    }

    public class CustomFields
    {
        public PixelPoint pixel_point { get; set; }
        public DeliveryHero? delivery_hero { get; set; }
    }

    public class DealLine
    {
        public string? deal_key { get; set; }
        public string? position { get; set; }
        public string? label { get; set; }
        public string? pricing_effect { get; set; }
        public string? pricing_value { get; set; }
    }

    public class Deal
    {
        public string? name { get; set; }
        public string? @ref { get; set; }
    }

    public class Info
    {
        public string? transaction_id { get; set; }
        public string? transaction_amount { get; set; }
        public string? transaction_currency { get; set; }
        public string? transaction_receipt { get; set; }
        public string? transaction_source { get; set; }
        public string? transaction_terminal { get; set; }
        public string? transaction_type { get; set; }
        public string? transaction_authCode { get; set; }
        public DateTime? order_time { get; set; }
        public string? order_status { get; set; }
    }




    public class Payment
    {
        public string? id { get; set; }
        public string? private_ref { get; set; }
        public string? type { get; set; }
        public string? name { get; set; }
        public string? @ref { get; set; }
        public string? amount { get; set; }
        public Info info { get; set; }
        public bool? deleted { get; set; }
    }
}
