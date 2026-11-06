using System;
using System.Collections.Generic;

namespace WeCreatives_EOI.Classes
{
 
    public class TabOrderRoot
    {
        public Dictionary<string, CartItem>? cart { get; set; }
        public string? orderType { get; set; }
        public string? table_id { get; set; }
        public string? paymentMethod { get; set; }
        public CustomerInfo? customerInfo { get; set; }
        public string? order_number { get; set; }
    }

    public class CartItem
    {
        public string? key { get; set; }
        public decimal? price { get; set; }
        public decimal? basePrice { get; set; }
        public string? name { get; set; }
        public string? image { get; set; }
        public string? pos_code { get; set; }
        public string? ref_code { get; set; }
        public int? quantity { get; set; }
        public CartDetails? details { get; set; }
        public List<object>? options { get; set; }
    }

    public class CartDetails
    {
        // Deal case
        public Dictionary<string, SlotItem> slots { get; set; }
        public decimal? basePrice { get; set; }
        public int? quantity { get; set; }

        // Simple item case
        public int? id { get; set; }
        public string name { get; set; }
        public decimal? price { get; set; }
        public string pos_code { get; set; }
        public string ref_code { get; set; }
        public List<ToppingItem> selected_toppings { get; set; }

        public VariantDetail crust { get; set; }
        public bool? isHalfAndHalf { get; set; }
        public HalfDetails firstHalf { get; set; }
        public HalfDetails secondHalf { get; set; }
    }

    public class SlotItem
    {
        public int id { get; set; }
        public string name { get; set; }
        public decimal? price { get; set; }
        public string ref_code { get; set; }
        public string pos_code { get; set; }
        public List<ToppingItem> selected_toppings { get; set; }

        public VariantDetail crust { get; set; }
        public bool? isHalfAndHalf { get; set; }
        public HalfDetails firstHalf { get; set; }
        public HalfDetails secondHalf { get; set; }
    }

    public class HalfDetails
    {
        public VariantDetail pizza { get; set; }
        public VariantDetail crust { get; set; }
        public List<VariantDetail> toppings { get; set; }
    }

    public class VariantDetail
    {
        public string variant_id { get; set; }
        public string variant_name { get; set; }
        public int? option_id { get; set; }
        public string option_name { get; set; }
        public string heading { get; set; }
        public int? item_id { get; set; }
        public string item_name { get; set; }
        public decimal? price { get; set; }
        public decimal? quantity { get; set; }
        public string pos_code { get; set; }
        public string ref_code { get; set; }
    }

    public class ToppingItem
    {
        public int? id { get; set; }
        public string name { get; set; }
        public decimal? price { get; set; }
        public int? quantity { get; set; }
        public string pos_code { get; set; }
        public string ref_code { get; set; }
    }
    public class Product
    {
        public int? id { get; set; }
        public int? category_id { get; set; }
        public string? name { get; set; }
        public string? slug { get; set; }
        public string? image { get; set; }
        public string? ref_code { get; set; }
        public string? pos_code { get; set; }
        public decimal? price { get; set; }
    }

    public class VariantItem
    {
        public int? id { get; set; }
        public int? variant_option_item_id { get; set; }
        public string? name { get; set; }
        public string? ref_code { get; set; }
        public string? pos_code { get; set; }
        public decimal? price { get; set; }
        public VariantOption? option { get; set; }
        public Variant? variant { get; set; }
    }

    public class VariantOption
    {
        public int? id { get; set; }
        public string? name { get; set; }
    }

    public class Variant
    {
        public int? id { get; set; }
        public string? name { get; set; }
    }

    public class CustomerInfo
    {
        public int? id { get; set; }
        public string? name { get; set; }
        public string? email { get; set; }
    }
}