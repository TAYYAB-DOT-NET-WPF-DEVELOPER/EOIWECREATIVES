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
    public static class FPOrderProcess
    {
        private const int COINIT_APARTMENTTHREADED = 0x2;

        public const string FP_SALE_TYPE_NUM = "734005201";
        public const string FP_PAYMENT_METHOD = "734005201";
        public const string FP_DISCOUNT_REF = "734003537";

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        /// <summary>
        /// Converts an FPRoot order into PixelPoint products, creates the request XML,
        /// checks duplicate orders, and sends the order to PixelPoint.
        /// </summary>
        public static string PlaceOrderdt(FPRoot root, string uniqueid, string orderid)
        {
            if (root == null)
            {
                return "Error: FPRoot order cannot be null.";
            }

            bool comInitialized = false;

            try
            {
                ValidateFPOrder(root);

                List<ProductModel> products = BuildFPProducts(root);

                if (products.Count == 0)
                {
                    return "Error: No valid products were found in the Foodpanda order.";
                }

                string referenceId = BuildReferenceId(root, orderid);

                string xml = CreateFPOrderXml(
                    root,
                    products,
                    FP_SALE_TYPE_NUM,
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
                        "Foodpanda order already exists in PixelPoint. ReferenceId: {ReferenceId}",
                        referenceId
                    );

                    FPupdatetabel(uniqueid, "11");

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
                    "PlaceOrderdt failed for FPRoot. OrderId: {OrderId}, UniqueId: {UniqueId}, Code: {Code}",
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

        /// <summary>
        /// Sends a pre-generated final_xml request directly to PixelPoint COM.
        /// </summary>
        public static string PlaceOrderFromXml(string finalXml, string uniqueid, string referenceId)
        {
            if (string.IsNullOrWhiteSpace(finalXml))
            {
                return "Error: final_xml cannot be empty.";
            }

            bool comInitialized = false;

            try
            {
                int comResult = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);

                // S_OK = 0 and S_FALSE = 1 both mean COM is initialized.
                comInitialized = comResult == 0 || comResult == 1;

                if (!comInitialized && (uint)comResult != 0x80010106)
                {
                    throw new InvalidOperationException(
                        $"COM initialization failed. HRESULT: 0x{comResult:X8}"
                    );
                }

                LogXMLToFile(finalXml, "IN");

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
                        "Foodpanda order already exists in PixelPoint. ReferenceId: {ReferenceId}",
                        referenceId
                    );

                    FPupdatetabel(uniqueid, "11");

                    return CreateDuplicateResponse(referenceId);
                }

                PixPosTrans pixPosTrans = new PixPosTrans();
                Task<string> processTask = Task.Run(() => pixPosTrans.ProcessRequest(finalXml));
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
                    "PlaceOrderFromXml failed. ReferenceId: {ReferenceId}, UniqueId: {UniqueId}",
                    referenceId,
                    uniqueid
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

        private static void ValidateFPOrder(FPRoot root)
        {
            if (root.Products == null || root.Products.Count == 0)
            {
                throw new InvalidOperationException(
                    "The Foodpanda order does not contain any products."
                );
            }
        }

        private static List<ProductModel> BuildFPProducts(FPRoot root)
        {
            var products = new List<ProductModel>();

            foreach (FPProduct fpProduct in root.Products ?? new List<FPProduct>())
            {
                string rawCode = FirstNotEmpty(
                    fpProduct.RemoteCode,
                    fpProduct.Id
                );
                string productCode = CleanProductCode(rawCode);

                if (string.IsNullOrWhiteSpace(productCode))
                {
                    Log.Warning(
                        "Skipping Foodpanda product because id and remoteCode are empty. Product: {ProductName}",
                        fpProduct.Name
                    );

                    continue;
                }

                string itemId = NextItemId(products);
                decimal quantity = ParsePositiveQuantity(fpProduct.Quantity);
                decimal unitPrice = GetNumericPart(
                    FirstNotEmpty(fpProduct.UnitPrice, fpProduct.PaidPrice)
                );

                var product = new ProductModel
                {
                    prodNum = productCode,
                    CostEach = FormatNumber(unitPrice),
                    PriceApplied = "13",
                    itemId = itemId,
                    parentId = itemId,
                    quantity = FormatNumber(quantity),
                    Description = fpProduct.Name
                };

                products.Add(product);

                AddJsonSelections(
                    products,
                    fpProduct.SelectedToppings,
                    product.itemId,
                    quantity,
                    "Selected Topping"
                );

                AddJsonSelections(
                    products,
                    fpProduct.SelectedChoices,
                    product.itemId,
                    quantity,
                    "Selected Choice"
                );
            }

            // Tackling discounts for Foodpanda as requested
            if (root.Discounts != null)
            {
                foreach (var discount in root.Discounts)
                {
                    string idd = (products.Count + 1).ToString();
                    string rawRef = !string.IsNullOrWhiteSpace(discount.Ref) ? discount.Ref : FP_DISCOUNT_REF;
                    string cleanCouponNum = CleanProductCode(rawRef);

                    ProductModel deal4 = new ProductModel
                    {
                        couponNum = cleanCouponNum,
                        itemId = idd,
                        parentId = idd,
                        CostEach = "-" + GetNumericPart(discount.PriceOff).ToString(CultureInfo.InvariantCulture),
                        PriceApplied = "13",
                        quantity = "1",
                        Description = discount.Name
                    };
                    products.Add(deal4);
                }
            }

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

                string rawCode = FirstNotEmpty(
                    GetPropertyString(selection, "remoteCode"),
                    GetPropertyString(selection, "id"),
                    GetPropertyString(selection, "code")
                );
                string code = CleanProductCode(rawCode);

                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                string name = GetPropertyString(selection, "name");
                decimal selectionUnitPrice = GetNumericPart(
                    FirstNotEmpty(
                        GetPropertyString(selection, "unitPrice"),
                        GetPropertyString(selection, "price"),
                        GetPropertyString(selection, "paidPrice")
                    )
                );

                decimal selectionQuantity = ParsePositiveQuantity(
                    FirstNotEmpty(
                        GetPropertyString(selection, "quantity"),
                        GetPropertyString(selection, "count")
                    )
                );

                decimal totalQuantity = Math.Max(1m, parentQuantity * selectionQuantity);
                string selectionItemId = NextItemId(products);

                products.Add(new ProductModel
                {
                    prodNum = code,
                    CostEach = FormatNumber(selectionUnitPrice),
                    PriceApplied = "13",
                    itemId = selectionItemId,
                    parentId = parentId,
                    quantity = FormatNumber(totalQuantity),
                    Description = string.IsNullOrWhiteSpace(name)
                        ? descriptionPrefix
                        : name
                });
            }
        }

        private static string CreateFPOrderXml(
            FPRoot root,
            List<ProductModel> products,
            string saleTypeNum,
            string referenceId)
        {
            XElement paymentsElement = new XElement("Payments");
            
            // Foodpanda Payment Method requirement: 734005201
            decimal grandTotal = GetNumericPart(root.Price?.GrandTotal);
            paymentsElement.Add(new XElement("Payment",
                new XElement("MethodNumber", FP_PAYMENT_METHOD),
                new XElement("Tender", grandTotal.ToString(CultureInfo.InvariantCulture)),
                new XElement("AuthType", "114")
            ));

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

        private static XElement CreateMemberXml(FPRoot root)
        {
            FPAddress? address = root.Delivery?.Address;

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

        private static string BuildReferenceId(FPRoot root, string orderid)
        {
            if (!string.IsNullOrWhiteSpace(orderid))
            {
                return orderid.Trim();
            }

            if (!string.IsNullOrWhiteSpace(root.Code))
            {
                return root.Code.Trim();
            }

            return $"FP-{root.ShortCode ?? Guid.NewGuid().ToString("N")}";
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
                        "PixelPoint rejected Foodpanda order. ReferenceId: {ReferenceId}, ErrorNum: {ErrorNum}, Error: {ErrorDescription}, Response: {ResponseXml}",
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

        public static void FPupdatetabel(
            string uniqueid,
            string transact)
        {
            const string query = @"
            UPDATE PJFPORDER
               SET STATUS = 1,
                   TRANSACT = @TRANSACT
             WHERE UNIQUEID = @UNIQUEID";

            SqlHelper.CURD(
                query,
                new SqlParameter("@TRANSACT", transact ?? string.Empty),
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
                    "FP",
                    currentDate
                );

                Directory.CreateDirectory(directoryPath);

                string safeStatus = Regex.Replace(
                    orderStatus ?? "UNKNOWN",
                    @"[^A-Za-z0-9_-]",
                    "_"
                );

                string fileName =
                    $"Log_FP_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}_{safeStatus}.xml";

                string filePath = Path.Combine(directoryPath, fileName);

                File.WriteAllText(
                    filePath,
                    xmlContent ?? string.Empty
                );

                Log.Information(
                    "Foodpanda XML logged to file: {FilePath}",
                    filePath
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to log Foodpanda XML to file.");
            }
        }

        private static void AddElementIfNotEmpty(
            XElement parent,
            string elementName,
            string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parent.Add(new XElement(elementName, value.Trim()));
            }
        }

        private static string NextItemId(List<ProductModel> products)
        {
            return (products.Count + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static decimal ParsePositiveQuantity(string? rawQuantity)
        {
            decimal numericQuantity = GetNumericPart(rawQuantity);
            return numericQuantity > 0 ? numericQuantity : 1m;
        }

        private static decimal GetNumericPart(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return 0m;
            }

            Match match = Regex.Match(input, @"[-+]?\d+(?:\.\d+)?");

            if (!match.Success)
            {
                return 0m;
            }

            return decimal.TryParse(
                match.Value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal parsed
            )
                ? parsed
                : 0m;
        }

        private static string FormatNumber(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FirstNotEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string GetPropertyString(
            JsonElement element,
            string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return ConvertJsonValueToString(property.Value);
                }
            }

            return string.Empty;
        }

        private static string ConvertJsonValueToString(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        private static string EscapeSqlLiteral(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static string CleanProductCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            string trimmed = code.Trim();
            if (trimmed.StartsWith("DEAL-", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(5).Trim();
            }

            return trimmed;
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
