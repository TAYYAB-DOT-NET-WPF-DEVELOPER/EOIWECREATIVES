namespace WeCreatives_EOI.Classes
{
    public class ProductModel
    {
        public string? prodNum { get; set; }
        public string? couponNum { get; set; }
        public string? itemId { get; set; }
        public string? parentId { get; set; }
        public string? quantity { get; set; }
        public string? ComboItemId { get; set; }
        public string? PRODTYPE { get; set; }
        public string? AnswerId { get; set; }
        public string? AnswerPrice { get; set; }
        public string? CostEach { get; set; }
        public string? TypeOfProd { get; set; }
        public string? NoTax { get; set; }
        public string? PriceApplied { get; set; }
        public string? Description { get; set; }
    }

    public class MemberModel
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? HomeTele { get; set; }
        public string? Email { get; set; }
        public string? Adress1 { get; set; }
        public string? Adress2 { get; set; }
        public string? Prov { get; set; }
        public string? Directions { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? Memcode { get; set; }
        public string? SaveMemberInfoToDB { get; set; }
    }
}

