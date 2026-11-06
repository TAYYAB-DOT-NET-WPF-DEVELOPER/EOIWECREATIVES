using PixelPointPOS;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
namespace WeCreatives_EOI.Classes
{
        public static class WebOrderProcess
        {
            private const int COINIT_APARTMENTTHREADED = 0x2;

            // IMPORTANT: Confirm these two values with your PixelPoint SaleType setup.
            // They are kept in one place so they can be changed easily.
            private const string WEB_DELIVERY_SALE_TYPE_NUM = "1002";
            private const string WEB_PICKUP_SALE_TYPE_NUM = "1003";

            [DllImport("ole32.dll")]
            private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

            [DllImport("ole32.dll")]
            private static extern void CoUninitialize();

            /// <summary>
            /// Converts a WebRoot order into PixelPoint products, creates the request XML,
            /// checks duplicate orders, and sends the order to PixelPoint.
            /// </summary>
            public static string PlaceOrderdt(WebRoot root, string uniqueid, string orderid)
            {
                if (root == null)
                {
                    return "Error: WebRoot order cannot be null.";
                }

                bool comInitialized = false;

                try
                {
                    ValidateWebOrder(root);

                    List<ProductModel> products = BuildWebProducts(root);

                    if (products.Count == 0)
                    {
                        return "Error: No valid products were found in the web order.";
                    }

                    string referenceId = BuildReferenceId(root, orderid);
                    string saleTypeNum = ResolveSaleTypeNum(root.ExpeditionType);

                    string xml = CreateWebOrderXml(
                        root,
                        products,
                        saleTypeNum,
                        referenceId
                    );

                    int comResult = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);

                    // S_OK = 0 and S_FALSE = 1 both mean COM is initialized.
                    comInitialized = comResult == 0 || comResult == 1;

                    if (!comInitialized && (uint)comResult != 0x80010106)
                    {
                        throw new InvalidOperationException(
                            $"COM initialization failed. HRESULT: 0x{comResult:X8}"
                        );
                    }

                    LogXMLToFile(xml, "IN");

                    string responseXml;
                    string safeReferenceId = EscapeSqlLiteral(referenceId);

                    string duplicateQuery = $@"
                    SELECT TRANSACT
                    FROM DBA.POSHEADER
                    WHERE REFID = '{safeReferenceId}'
                      AND OPENDATE = (SELECT MAX(OPENDATE) FROM DBA.POSHEADER)";

                    bool alreadyExists = ODBCHelper.ExistRec(duplicateQuery);

                    if (alreadyExists)
                    {
                        Log.Information(
                            "Web order already exists in PixelPoint. ReferenceId: {ReferenceId}",
                            referenceId
                        );

                        updatetabel(uniqueid, orderid, "11");

                        return CreateDuplicateResponse(referenceId);
                    }

                    PixPosTrans pixPosTrans = new PixPosTrans();
                    Task<string> processTask = Task.Run(() => pixPosTrans.ProcessRequest(xml));
                    if (Task.WaitAny(new Task[] { processTask }, TimeSpan.FromSeconds(15)) != 0)
                    {
                        Log.Warning("PixelPoint COM request timed out after 15 seconds. ReferenceId: {ReferenceId}", referenceId);
                        throw new TimeoutException("PixelPoint POS COM transaction request timed out after 15 seconds.");
                    }
                    responseXml = processTask.Result;

                    if (string.IsNullOrWhiteSpace(responseXml))
                    {
                        throw new InvalidOperationException(
                            "PixelPoint returned an empty response."
                        );
                    }

                    LogXMLToFile(responseXml, "OUT");
                    LogPixelPointError(responseXml, referenceId);

                    return responseXml;
                }
                catch (Exception ex)
                {
                    Log.Error(
                        ex,
                        "PlaceOrderdt failed for WebRoot. OrderId: {OrderId}, UniqueId: {UniqueId}, Code: {Code}",
                        orderid,
                        uniqueid,
                        root?.Code
                    );

                    return $"Error: {ex.Message}";
                }
                finally
                {
                    if (comInitialized)
                    {
                        CoUninitialize();
                    }
                }
            }

            private static void ValidateWebOrder(WebRoot root)
            {
                if (root.Products == null || root.Products.Count == 0)
                {
                    throw new InvalidOperationException(
                        "The web order does not contain any products."
                    );
                }

                if (root.PlatformRestaurant == null ||
                    string.IsNullOrWhiteSpace(root.PlatformRestaurant.Id))
                {
                    throw new InvalidOperationException(
                        "platformRestaurant.id is missing in the web order."
                    );
                }

                if (root.Customer == null)
                {
                    throw new InvalidOperationException(
                        "Customer information is missing in the web order."
                    );
                }
            }

            private static List<ProductModel> BuildWebProducts(WebRoot root)
            {
                var products = new List<ProductModel>();

                foreach (WebProduct webProduct in root.Products ?? new List<WebProduct>())
                {
                    string productCode = FirstNotEmpty(
                        webProduct.RemoteCode,
                        webProduct.Id
                    );

                    if (string.IsNullOrWhiteSpace(productCode))
                    {
                        Log.Warning(
                            "Skipping web product because id and remoteCode are empty. Product: {ProductName}",
                            webProduct.Name
                        );

                        continue;
                    }

                    string itemId = NextItemId(products);
                    decimal quantity = ParsePositiveQuantity(webProduct.Quantity);
                    decimal unitPrice = GetNumericPart(
                        FirstNotEmpty(webProduct.UnitPrice, webProduct.PaidPrice)
                    );

                    var product = new ProductModel
                    {
                        prodNum = productCode.Trim(),
                        CostEach = FormatNumber(unitPrice),
                        PriceApplied = "13",
                        itemId = itemId,
                        parentId = itemId,
                        quantity = FormatNumber(quantity),
                        Description = webProduct.Name
                    };

                    products.Add(product);

                    AddJsonSelections(
                        products,
                        webProduct.SelectedToppings,
                        product.itemId,
                        quantity,
                        "Selected Topping"
                    );

                    AddJsonSelections(
                        products,
                        webProduct.SelectedChoices,
                        product.itemId,
                        quantity,
                        "Selected Choice"
                    );

                    AddProductDiscounts(
                        products,
                        webProduct.Discounts,
                        product.itemId
                    );
                }

                AddOrderDiscounts(products, root.Discounts);
                AddDeliveryFee(products, root);

                return products;
            }

            private static void AddJsonSelections(
                List<ProductModel> products,
                List<JsonElement>? selections,
                string parentId,
                decimal parentQuantity,
                string descriptionPrefix)
            {
                if (selections == null || selections.Count == 0)
                {
                    return;
                }

                foreach (JsonElement selection in selections)
                {
                    if (selection.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string code = FirstNotEmpty(
                        GetJsonString(selection, "remoteCode"),
                        GetJsonString(selection, "ref"),
                        GetJsonString(selection, "id"),
                        GetJsonString(selection, "code")
                    );

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        Log.Warning(
                            "Skipping {DescriptionPrefix} because no product code was found. JSON: {Selection}",
                            descriptionPrefix,
                            selection.ToString()
                        );

                        continue;
                    }

                    decimal price = GetNumericPart(
                        FirstNotEmpty(
                            GetJsonString(selection, "paidPrice"),
                            GetJsonString(selection, "unitPrice"),
                            GetJsonString(selection, "price")
                        )
                    );

                    decimal quantity = ParseQuantityOrDefault(
                        GetJsonString(selection, "quantity"),
                        parentQuantity
                    );

                    bool removed = GetJsonBoolean(selection, "removed") ||
                                   GetJsonBoolean(selection, "Removed");

                    if (removed)
                    {
                        quantity = -Math.Abs(quantity == 0 ? parentQuantity : quantity);
                    }

                    string itemId = NextItemId(products);

                    products.Add(new ProductModel
                    {
                        prodNum = code.Trim(),
                        CostEach = FormatNumber(price),
                        PriceApplied = "13",
                        itemId = itemId,
                        parentId = parentId,
                        quantity = FormatNumber(quantity),
                        Description = FirstNotEmpty(
                            GetJsonString(selection, "name"),
                            descriptionPrefix
                        )
                    });

                    // Recursively process any nested children, selectedToppings, selectedChoices, or options
                    ProcessNestedJsonSelections(
                        products,
                        selection,
                        itemId,
                        quantity,
                        descriptionPrefix
                    );
                }
            }

            private static void ProcessNestedJsonSelections(
                List<ProductModel> products,
                JsonElement parentSelection,
                string parentItemId,
                decimal parentQuantity,
                string descriptionPrefix)
            {
                string[] childPropertyNames = new[] { "children", "selectedToppings", "selectedChoices", "options" };

                foreach (string propName in childPropertyNames)
                {
                    if (parentSelection.TryGetProperty(propName, out JsonElement childProp) &&
                        childProp.ValueKind == JsonValueKind.Array)
                    {
                        var childElements = new List<JsonElement>();
                        foreach (JsonElement elem in childProp.EnumerateArray())
                        {
                            childElements.Add(elem);
                        }

                        if (childElements.Count > 0)
                        {
                            AddJsonSelections(
                                products,
                                childElements,
                                parentItemId,
                                parentQuantity,
                                descriptionPrefix
                            );
                        }
                    }
                }
            }


            private static void AddProductDiscounts(
                List<ProductModel> products,
                List<JsonElement>? discounts,
                string parentId)
            {
                if (discounts == null || discounts.Count == 0)
                {
                    return;
                }

                foreach (JsonElement discount in discounts)
                {
                    AddDiscount(products, discount, parentId);
                }
            }

            private static void AddOrderDiscounts(
                List<ProductModel> products,
                List<JsonElement>? discounts)
            {
                if (discounts == null || discounts.Count == 0)
                {
                    return;
                }

                foreach (JsonElement discount in discounts)
                {
                    string itemId = NextItemId(products);
                    AddDiscount(products, discount, itemId);
                }
            }

            private static void AddDiscount(
                List<ProductModel> products,
                JsonElement discount,
                string parentId)
            {
                if (discount.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                string couponCode = FirstNotEmpty(
                    GetJsonString(discount, "remoteCode"),
                    GetJsonString(discount, "ref"),
                    GetJsonString(discount, "code"),
                    GetJsonString(discount, "id")
                );

                decimal amount = GetNumericPart(
                    FirstNotEmpty(
                        GetJsonString(discount, "priceOff"),
                        GetJsonString(discount, "price_off"),
                        GetJsonString(discount, "amount"),
                        GetJsonString(discount, "value")
                    )
                );

                if (string.IsNullOrWhiteSpace(couponCode) && amount == 0)
                {
                    return;
                }

                string itemId = NextItemId(products);

                products.Add(new ProductModel
                {
                    couponNum = couponCode,
                    CostEach = FormatNumber(-Math.Abs(amount)),
                    PriceApplied = "13",
                    itemId = itemId,
                    parentId = string.IsNullOrWhiteSpace(parentId) ? itemId : parentId,
                    quantity = "1",
                    Description = FirstNotEmpty(
                        GetJsonString(discount, "name"),
                        "Web Order Discount"
                    )
                });
            }

            private static void AddDeliveryFee(
                List<ProductModel> products,
                WebRoot root)
            {
                decimal deliveryFee = GetNumericPart(root.Price?.DeliveryFee);

                if (deliveryFee <= 0)
                {
                    return;
                }

                string deliveryProductCode = Convert.ToString(
                    App.FPPRODNUM,
                    CultureInfo.InvariantCulture
                ) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(deliveryProductCode))
                {
                    Log.Warning(
                        "Delivery fee {DeliveryFee} was received, but App.FPPRODNUM is empty. Delivery fee item was not added.",
                        deliveryFee
                    );

                    return;
                }

                string itemId = NextItemId(products);

                products.Add(new ProductModel
                {
                    prodNum = deliveryProductCode,
                    CostEach = FormatNumber(deliveryFee),
                    PriceApplied = "13",
                    itemId = itemId,
                    parentId = itemId,
                    quantity = "1",
                    Description = "Web Order Delivery Fee"
                });
            }

            private static string CreateWebOrderXml(
                WebRoot root,
                List<ProductModel> products,
                string saleTypeNum,
                string referenceId)
            {
                XElement paymentsElement = new XElement("Payments");
                if (root.Payment != null && !string.IsNullOrWhiteSpace(root.Payment.RemoteCode))
                {
                    string paymentCode = root.Payment.RemoteCode.Trim();
                    if (paymentCode != "1001")
                    {
                        paymentsElement.Add(new XElement("Payment",
                            new XElement("MethodNumber", paymentCode),
                            new XElement("Tender", GetNumericPart(root.Price?.GrandTotal).ToString(CultureInfo.InvariantCulture)),
                            new XElement("AuthType", "114")
                        ));
                    }
                }

                var request = new XElement("Request",
                    new XElement("RequestType", "10"),
                    new XElement("DebugMode", "1"),
                    new XElement("Transaction",
                        new XElement("SaleTypeNum", saleTypeNum),
                        new XElement("EXTRAREFID", "2146437419"),
                        new XElement("ReferenceId", referenceId),
                        paymentsElement,
                        new XElement("items",
                            products.Select(CreateProductXml)
                        )
                    ),
                    CreateMemberXml(root)
                );

                return new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    request
                ).ToString();
            }

            private static XElement CreateProductXml(ProductModel product)
            {
                var item = new XElement("item");

                AddElementIfNotEmpty(item, "prodNum", product.prodNum);
                AddElementIfNotEmpty(item, "couponNum", product.couponNum);
                AddElementIfNotEmpty(item, "itemId", product.itemId);
                AddElementIfNotEmpty(item, "parentId", product.parentId);
                AddElementIfNotEmpty(item, "quantity", product.quantity);
                AddElementIfNotEmpty(item, "CostEach", product.CostEach);
                AddElementIfNotEmpty(item, "PriceApplied", product.PriceApplied);
                AddElementIfNotEmpty(item, "AnswerId", product.AnswerId);

                return item;
            }

            private static XElement CreateMemberXml(WebRoot root)
            {
                WebAddress? address = root.Delivery?.Address;

                string address1 = string.Join(
                    " ",
                    new[] { address?.Street, address?.Number }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                );

                return new XElement("Member",
                    new XElement("FirstName", root.Customer?.FirstName ?? string.Empty),
                    new XElement("LastName", root.Customer?.LastName ?? string.Empty),
                    new XElement("HomeTele", CleanPhoneNumber(root.Customer?.MobilePhone)),
                    new XElement("Address1", address1),
                    new XElement("Address2", address?.District ?? string.Empty),
                    new XElement("City", address?.City ?? string.Empty),
                    new XElement("Email", root.Customer?.Email ?? string.Empty),
                    new XElement("Directions", root.Comments?.CustomerComment ?? string.Empty),
                    new XElement("Country", string.Empty)
                );
            }

            private static string ResolveSaleTypeNum(WebExpeditionType? expeditionType)
            {
                if (expeditionType != null && !string.IsNullOrWhiteSpace(expeditionType.RemoteCode))
                {
                    return expeditionType.RemoteCode.Trim();
                }

                if (expeditionType != null && string.Equals(expeditionType.Type, "pickup", StringComparison.OrdinalIgnoreCase))
                {
                    return WEB_PICKUP_SALE_TYPE_NUM;
                }

                return WEB_DELIVERY_SALE_TYPE_NUM;
            }

            private static string BuildReferenceId(WebRoot root, string orderid)
            {
                if (!string.IsNullOrWhiteSpace(orderid))
                {
                    return orderid.Trim();
                }

                return $"WEB-{root.PlatformRestaurant?.Id}-{root.Code}";
            }

            private static void LogPixelPointError(
                string responseXml,
                string referenceId)
            {
                try
                {
                    XDocument xmlDocument = XDocument.Parse(responseXml);

                    int errorNumber = int.TryParse(
                        xmlDocument.Descendants("ErrorNum").FirstOrDefault()?.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int parsedErrorNumber
                    )
                        ? parsedErrorNumber
                        : 0;

                    if (errorNumber != 0)
                    {
                        string errorDescription =
                            xmlDocument.Descendants("ErrorDescription")
                                .FirstOrDefault()?.Value
                            ?? xmlDocument.Descendants("ErrorDesc")
                                .FirstOrDefault()?.Value
                            ?? "Unknown PixelPoint error";

                        Log.Error(
                            "PixelPoint rejected web order. ReferenceId: {ReferenceId}, ErrorNum: {ErrorNum}, Error: {ErrorDescription}, Response: {ResponseXml}",
                            referenceId,
                            errorNumber,
                            errorDescription,
                            responseXml
                        );
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Unable to parse PixelPoint response XML. ReferenceId: {ReferenceId}, Response: {ResponseXml}",
                        referenceId,
                        responseXml
                    );
                }
            }

            private static string CreateDuplicateResponse(string referenceId)
            {
                return new XDocument(
                    new XElement("Response",
                        new XElement("ErrorNum", "0"),
                        new XElement("Status", "AlreadyExists"),
                        new XElement("ReferenceId", referenceId),
                        new XElement(
                            "Message",
                            "Order already exists in PixelPoint."
                        )
                    )
                ).ToString();
            }

            public static void updatetabel(
                string uniqueid,
                string orderid,
                string transact)
            {
                const string query = @"
                UPDATE EOIHQ
                   SET STATUS = 1,
                       TRANSACT = @TRANSACT
                 WHERE ORDERID = @ORDERID
                   AND UNIQUEID = @UNIQUEID";

                SqlHelper.CURD(
                    query,
                    new SqlParameter("@TRANSACT", transact ?? string.Empty),
                    new SqlParameter("@ORDERID", orderid ?? string.Empty),
                    new SqlParameter("@UNIQUEID", uniqueid ?? string.Empty)
                );
            }

            public static void LogXMLToFile(
                string xmlContent,
                string orderStatus)
            {
                try
                {
                    string currentDate = DateTime.Now.ToString(
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture
                    );

                    string directoryPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "Logs",
                        "Orders",
                        currentDate
                    );

                    Directory.CreateDirectory(directoryPath);

                    string safeStatus = Regex.Replace(
                        orderStatus ?? "UNKNOWN",
                        @"[^A-Za-z0-9_-]",
                        "_"
                    );

                    string fileName =
                        $"Log_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}_{safeStatus}.xml";

                    string filePath = Path.Combine(directoryPath, fileName);

                    File.WriteAllText(
                        filePath,
                        xmlContent ?? string.Empty
                    );

                    Log.Information(
                        "XML logged to file: {FilePath}",
                        filePath
                    );
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to log XML to file.");
                }
            }

            public static decimal GetNumericPart(string? input)
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    return 0m;
                }

                Match match = Regex.Match(
                    input,
                    @"[-+]?\d*\.?\d+"
                );

                if (!match.Success)
                {
                    return 0m;
                }

                return decimal.TryParse(
                    match.Value,
                    NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out decimal result
                )
                    ? result
                    : 0m;
            }

            private static decimal ParsePositiveQuantity(string? quantity)
            {
                decimal parsedQuantity = GetNumericPart(quantity);
                return parsedQuantity > 0 ? parsedQuantity : 1m;
            }

            private static decimal ParseQuantityOrDefault(
                string? quantity,
                decimal defaultQuantity)
            {
                decimal parsedQuantity = GetNumericPart(quantity);
                return parsedQuantity == 0 ? defaultQuantity : parsedQuantity;
            }

            private static string FormatNumber(decimal value)
            {
                return value.ToString("0.####", CultureInfo.InvariantCulture);
            }

            private static string NextItemId(List<ProductModel> products)
            {
                return (products.Count + 1).ToString(CultureInfo.InvariantCulture);
            }

            private static string FirstNotEmpty(params string?[] values)
            {
                return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                       ?? string.Empty;
            }

            private static void AddElementIfNotEmpty(
                XElement parent,
                string elementName,
                string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parent.Add(new XElement(elementName, value));
                }
            }

            private static string EscapeSqlLiteral(string value)
            {
                return (value ?? string.Empty).Replace("'", "''");
            }

            private static string GetJsonString(
                JsonElement element,
                string propertyName)
            {
                if (!TryGetJsonPropertyIgnoreCase(
                        element,
                        propertyName,
                        out JsonElement property))
                {
                    return string.Empty;
                }

                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                };
            }

            private static bool GetJsonBoolean(
                JsonElement element,
                string propertyName)
            {
                if (!TryGetJsonPropertyIgnoreCase(
                        element,
                        propertyName,
                        out JsonElement property))
                {
                    return false;
                }

                if (property.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                return bool.TryParse(
                    property.ToString(),
                    out bool parsedValue
                ) && parsedValue;
            }

            private static bool TryGetJsonPropertyIgnoreCase(
                JsonElement element,
                string propertyName,
                out JsonElement value)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (string.Equals(
                                property.Name,
                                propertyName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            value = property.Value;
                            return true;
                        }
                    }
                }

                value = default;
                return false;
            }

            private static string CleanPhoneNumber(string? phone)
            {
                if (string.IsNullOrWhiteSpace(phone))
                    return string.Empty;

                string trimmed = phone.Trim();
                if (trimmed.StartsWith("+92-", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(4).Trim();
                }
                if (trimmed.StartsWith("+92", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(3).Trim();
                }
                if (trimmed.StartsWith("92-", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(3).Trim();
                }

                return trimmed;
            }
        }
    }


