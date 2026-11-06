using PixelPointPOS;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WeCreatives_EOI.Classes
{
    public static class TabOrderProcess
    {
        private const int COINIT_APARTMENTTHREADED = 0x2;
        private const decimal HALF_QUANTITY = 0.5m;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        public static string TabPlaceOrderdt(TabOrderRoot root, string uniqueid, string orderid)
        {
            try
            {
                List<ProductModel> products = BuildProducts(root);

                if (products.Count == 0)
                    throw new InvalidOperationException("Cart se koi valid POS product build nahi hua.");

                bool comInitialized = false;
                try
                {
                    int comResult = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                    comInitialized = comResult == 0 || comResult == 1;

                    string xml = TabCreateXML.TabCreateXMLdt(root, products);
                    PixPosTrans pixPosTrans = new PixPosTrans();

                    LogXMLToFile(xml, "IN");

                    string responseXML;

                    try
                    {
                        string referenceId = GetReferenceId(root, uniqueid, orderid);

                        string query1 = @"
                            SELECT TRANSACT
                            FROM dba.posheader
                            WHERE refid = ?
                              AND opendate = (SELECT MAX(opendate) FROM dba.posheader)";

                        OdbcParameter[] parameters = new[] { new OdbcParameter("refid", OdbcType.VarChar) { Value = referenceId } };
                        bool exist = ODBCHelper.ExistRec(query1, parameters);

                        if (exist)
                        {
                            updatetabel(uniqueid, referenceId, "11");
                            responseXML = xml;
                        }
                            Task<string> processTask = Task.Run(() => pixPosTrans.ProcessRequest(xml));
                            if (Task.WaitAny(new Task[] { processTask }, TimeSpan.FromSeconds(15)) != 0)
                            {
                                Log.Warning("PixelPoint COM request timed out after 15 seconds.");
                                throw new TimeoutException("PixelPoint POS COM transaction request timed out after 15 seconds.");
                            }
                            responseXML = processTask.Result;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error while processing tab order request");
                        throw;
                    }

                    XDocument xmlDoc = XDocument.Parse(responseXML);

                    int errorNum;
                    int.TryParse(
                        xmlDoc.Descendants("ErrorNum").FirstOrDefault()?.Value ?? "0",
                        out errorNum
                    );

                    XElement transNumberElement = xmlDoc
                        .Descendants("Transaction")
                        .Elements("TransNumber")
                        .FirstOrDefault();

                    string transNumber = transNumberElement?.Value ?? "0";

                    if (errorNum == 0 && transNumber != "0")
                    {
                        UpdateTableInfoForTab(root?.table_id, transNumber);
                    }
                    else
                    {
                        Log.Information("PixelPoint response: {ResponseXML}", responseXML);
                    }

                    LogXMLToFile(responseXML, "OUT");

                    return responseXML;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "TabPlaceOrderdt encountered an error");
                    return $"Error: {ex.Message}";
                }
                finally
                {
                    if (comInitialized)
                    {
                        try
                        {
                            CoUninitialize();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Error during CoUninitialize in TabPlaceOrderdt.");
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, "TabPlaceOrderdt outer error");
                return $"Error: {ex.Message}";
            }
        }

        private static List<ProductModel> BuildProducts(TabOrderRoot root)
        {
            List<ProductModel> products = new List<ProductModel>();

            if (root?.cart == null || root.cart.Count == 0)
                return products;

            foreach (var cartRow in root.cart)
            {
                string cartKey = cartRow.Key;
                CartItem cartItem = cartRow.Value;

                if (cartItem == null)
                    continue;

                int cartQty = NormalizeQuantity(cartItem.quantity);
                object details = cartItem.details;

                /*
                 * SIMPLE HALF & HALF CASE
                 * Example: item-1 / Half & Half Medium
                 *
                 * Is case mein cart aur details ka direct POS code null ho sakta hai.
                 * Dono halves ko separate 0.5 quantity products ke taur par add kiya jayega.
                 */
                if (IsHalfAndHalf(details))
                {
                    decimal itemPrice = cartItem.price ?? GetDecimalProperty(details, "price");

                    AddHalfAndHalfToProducts(
                        products,
                        details,
                        parentItemId: null,
                        cartQty: cartQty,
                        requestedUnitPrice: itemPrice,
                        context: $"Cart item '{cartKey}'"
                    );

                    continue;
                }

                bool isDeal = HasSlots(details);

                string parentProdNum =
                    ResolveProdNum(cartItem.pos_code, cartItem.ref_code)
                    ?? ResolveObjectProdNum(details)
                    ?? ResolveProductCodeFromCrust(details);

                if (string.IsNullOrWhiteSpace(parentProdNum))
                {
                    string itemName = GetStringProperty(cartItem, "name", "Name");
                    throw new InvalidOperationException(
                        $"Cart item '{cartKey}' ({itemName}) ka POS/ref code missing hai."
                    );
                }

                string parentItemId = AddProduct(
                    products,
                    prodNum: parentProdNum,
                    costEach: GetParentPrice(cartItem),
                    parentItemId: null,
                    quantity: cartQty,
                    applyPrice: true
                );

                    /*
                     * DEAL CASE
                     * details.slots contains deal components.
                     */
                    if (isDeal)
                    {
                        foreach (var slotRow in cartItem.details.slots)
                        {
                            SlotItem slot = slotRow.Value;

                            if (slot == null)
                                continue;

                            string cartName = GetStringProperty(cartItem, "name", "Name");
                            string slotName = GetStringProperty(slot, "name", "Name");
                            string slotContext =
                                $"Deal '{cartName}', " +
                                $"slot '{slotRow.Key}' ({slotName})";

                        /*
                         * HALF & HALF INSIDE DEAL
                         * Example: Value Deal Medium -> slot 44.
                         */
                        if (IsHalfAndHalf(slot))
                        {
                            AddHalfAndHalfToProducts(
                                products,
                                slot,
                                parentItemId,
                                cartQty,
                                slot.price ?? 0,
                                slotContext
                            );

                            continue;
                        }

                        AddRegularSlotToProducts(
                            products,
                            slot,
                            parentItemId,
                            cartQty,
                            slotContext
                        );
                    }

                    continue;
                }

                /*
                 * SIMPLE REGULAR ITEM
                 * Add crust only when its code is different from the parent code.
                 */
                AddCrustIfDistinct(
                    products,
                    details,
                    parentProdNum,
                    parentItemId,
                    cartQty
                );

                AddModifiersToProducts(
                    products,
                    cartItem.details?.selected_toppings,
                    parentItemId,
                    cartQty
                );
            }

            Log.Information("Tab order product list built.Product count: {ProductCount}", products.Count);
            return products;
        }

        private static void AddRegularSlotToProducts(
            List<ProductModel> products,
            SlotItem slot,
            string dealParentItemId,
            int cartQty,
            string context)
        {
            string slotProdNum =
                ResolveProdNum(slot.pos_code, slot.ref_code)
                ?? ResolveProductCodeFromCrust(slot);

            if (string.IsNullOrWhiteSpace(slotProdNum))
            {
                throw new InvalidOperationException(
                    $"{context} ka POS/ref code missing hai."
                );
            }

            string slotItemId = AddProduct(
                products,
                prodNum: slotProdNum,
                costEach: slot.price ?? 0,
                parentItemId: dealParentItemId,
                quantity: cartQty,
                applyPrice: false
            );

            AddCrustIfDistinct(
                products,
                slot,
                slotProdNum,
                slotItemId,
                cartQty
            );

            AddModifiersToProducts(
                products,
                slot.selected_toppings,
                slotItemId,
                cartQty
            );
        }

        private static void AddHalfAndHalfToProducts(
            List<ProductModel> products,
            object halfContainer,
            string parentItemId,
            int cartQty,
            decimal requestedUnitPrice,
            string context)
        {
            object firstHalf = GetPropertyValue(halfContainer, "firstHalf", "FirstHalf");
                object secondHalf = GetPropertyValue(halfContainer, "secondHalf", "SecondHalf");

                            if (firstHalf == null || secondHalf == null)
                            {
                                throw new InvalidOperationException(
                                    $"{context} mein firstHalf ya secondHalf missing hai."
                                );
                            }

                            /*
                             * Resolution priority for each half:
                             * 1. Its own pizza POS/ref code
                             * 2. Its own crust POS/ref code
                             * 3. The other half's crust POS/ref code as a shared-crust fallback
                             *
                             * Example order 3422:
                             * first-half pizza/crust code is null, while second-half crust code is
                             * 2146435918. Because both halves use Authentic Thin, that crust code
                             * is used for the first half as well.
                             */
                            string sharedCrustProdNum =
                                ResolveHalfCrustProdNum(firstHalf)
                                ?? ResolveHalfCrustProdNum(secondHalf);

                            string firstProdNum =
                                ResolveHalfProdNum(firstHalf)
                                ?? sharedCrustProdNum;

                            string secondProdNum =
                                ResolveHalfProdNum(secondHalf)
                                ?? sharedCrustProdNum;

                            if (string.IsNullOrWhiteSpace(firstProdNum))
                            {
                                throw new InvalidOperationException(
                                    $"{context} ke first half ka pizza ya shared crust POS/ref code missing hai."
                                );
                            }

                            if (string.IsNullOrWhiteSpace(secondProdNum))
                            {
                                throw new InvalidOperationException(
                                    $"{context} ke second half ka pizza ya shared crust POS/ref code missing hai."
                                );
                            }

                            decimal firstUnitPrice = GetHalfUnitPrice(firstHalf);
                            decimal secondUnitPrice = GetHalfUnitPrice(secondHalf);

                            AdjustHalfPricesToRequestedTotal(
                                ref firstUnitPrice,
                                ref secondUnitPrice,
                                requestedUnitPrice
                            );

                            decimal halfQty = cartQty * HALF_QUANTITY;
                            bool topLevel = string.IsNullOrWhiteSpace(parentItemId);

                            string firstItemId = AddProduct(
                                products,
                                prodNum: firstProdNum,
                                costEach: firstUnitPrice,
                                parentItemId: topLevel ? null : parentItemId,
                                quantity: halfQty,
                                applyPrice: topLevel
                            );

                            AddHalfModifiers(
                                products,
                                firstHalf,
                                firstItemId,
                                cartQty
                            );

                            string secondItemId = AddProduct(
                                products,
                                prodNum: secondProdNum,
                                costEach: secondUnitPrice,
                                parentItemId: topLevel ? null : parentItemId,
                                quantity: halfQty,
                                applyPrice: topLevel
                            );

                            AddHalfModifiers(
                                products,
                                secondHalf,
                                secondItemId,
                                cartQty
                            );
                        }

        private static void AddHalfModifiers(
            List<ProductModel> products,
            object half,
            string halfItemId,
            int cartQty)
        {
            object toppings = GetPropertyValue(
                half,
                "toppings",
                "Toppings",
                "selected_toppings",
                "SelectedToppings"
            );

            AddModifiersToProducts(
                products,
                toppings,
                halfItemId,
                cartQty * HALF_QUANTITY
            );
        }

        private static void AddCrustIfDistinct(
            List<ProductModel> products,
            object item,
            string mainProdNum,
            string parentItemId,
            decimal quantity)
        {
            object crust = GetPropertyValue(item, "crust", "Crust");

            if (crust == null)
                return;

            string crustProdNum = ResolveObjectProdNum(crust);

            if (string.IsNullOrWhiteSpace(crustProdNum))
                return;

            /*
             * Most payloads mein crust.ref_code selected pizza SKU ke barabar hota hai.
             * Same code ko dobara product list mein add nahi karna.
             */
            if (string.Equals(
                crustProdNum,
                mainProdNum,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AddProduct(
                products,
                prodNum: crustProdNum,
                costEach: GetDecimalProperty(crust, "price", "Price"),
                parentItemId: parentItemId,
                quantity: quantity,
                applyPrice: false
            );
        }

        private static void AddModifiersToProducts(
            List<ProductModel> products,
            object modifiers,
            string parentItemId,
            decimal baseQuantity)
        {
            if (modifiers == null || string.IsNullOrWhiteSpace(parentItemId))
                return;

            IEnumerable modifierList = modifiers as IEnumerable;

            if (modifierList == null || modifiers is string)
                return;

            foreach (object modifier in modifierList)
            {
                if (modifier == null)
                    continue;

                string modifierProdNum = ResolveObjectProdNum(modifier);

                if (string.IsNullOrWhiteSpace(modifierProdNum))
                {
                    Log.Warning(
                        "Modifier skipped because POS/ref code is missing. Modifier: {ModifierName}",
                        GetStringProperty(modifier, "name", "Name", "item_name", "ItemName")
                    );
                    continue;
                }

                decimal modifierQty = GetDecimalProperty(modifier, "quantity", "Quantity");

                if (modifierQty <= 0)
                    modifierQty = 1;

                AddProduct(
                    products,
                    prodNum: modifierProdNum,
                    costEach: GetDecimalProperty(modifier, "price", "Price"),
                    parentItemId: parentItemId,
                    quantity: baseQuantity * modifierQty,
                    applyPrice: false
                );
            }
        }

        private static string AddProduct(
            List<ProductModel> products,
            string prodNum,
            decimal costEach,
            string parentItemId,
            decimal quantity,
            bool applyPrice)
        {
            if (string.IsNullOrWhiteSpace(prodNum))
                throw new ArgumentException("prodNum cannot be empty.", nameof(prodNum));

            if (quantity <= 0)
                quantity = 1;

            string itemId = (products.Count + 1).ToString(CultureInfo.InvariantCulture);

            ProductModel product = new ProductModel
            {
                prodNum = prodNum.Trim(),
                CostEach = ToAmount(costEach),
                itemId = itemId,
                parentId = string.IsNullOrWhiteSpace(parentItemId)
                    ? itemId
                    : parentItemId,
                quantity = ToQuantity(quantity)
            };

            if (applyPrice)
                product.PriceApplied = "13";

            products.Add(product);
            return itemId;
        }

        private static bool HasSlots(object details)
        {
            if (details == null)
                return false;

            object slots = GetPropertyValue(details, "slots", "Slots");

            if (slots is IDictionary dictionary)
                return dictionary.Count > 0;

            return false;
        }

        private static bool IsHalfAndHalf(object source)
        {
            if (source == null)
                return false;

            object explicitFlag = GetPropertyValue(
                source,
                "isHalfAndHalf",
                "IsHalfAndHalf"
            );

            if (explicitFlag != null && bool.TryParse(explicitFlag.ToString(), out bool isHalf))
            {
                if (isHalf)
                    return true;
            }

            return GetPropertyValue(source, "firstHalf", "FirstHalf") != null
                && GetPropertyValue(source, "secondHalf", "SecondHalf") != null;
        }

        private static string ResolveHalfProdNum(object half)
        {
            object pizza = GetPropertyValue(half, "pizza", "Pizza");

            return ResolveObjectProdNum(pizza)
                ?? ResolveHalfCrustProdNum(half);
        }

        private static string ResolveHalfCrustProdNum(object half)
        {
            object crust = GetPropertyValue(half, "crust", "Crust");
            return ResolveObjectProdNum(crust);
        }

        private static decimal GetHalfUnitPrice(object half)
        {
            object pizza = GetPropertyValue(half, "pizza", "Pizza");
            object crust = GetPropertyValue(half, "crust", "Crust");

            decimal pizzaPrice = GetDecimalProperty(pizza, "price", "Price");

            if (pizzaPrice > 0)
                return pizzaPrice;

            return GetDecimalProperty(crust, "price", "Price");
        }

        private static void AdjustHalfPricesToRequestedTotal(
            ref decimal firstUnitPrice,
            ref decimal secondUnitPrice,
            decimal requestedUnitPrice)
        {
            if (requestedUnitPrice <= 0)
                return;

            decimal currentUnitTotal =
                (firstUnitPrice * HALF_QUANTITY)
                + (secondUnitPrice * HALF_QUANTITY);

            if (currentUnitTotal <= 0)
            {
                /*
                 * Both halves have zero price, but slot/cart has an upcharge.
                 * Same unit price on both 0.5 lines keeps total equal to requestedUnitPrice.
                 */
                firstUnitPrice = requestedUnitPrice;
                secondUnitPrice = requestedUnitPrice;
                return;
            }

            if (Math.Abs(currentUnitTotal - requestedUnitPrice) <= 0.01m)
                return;

            /*
             * Preserve the payload's final cart/slot price while retaining relative
             * price weight between first and second half.
             */
            decimal scale = requestedUnitPrice / currentUnitTotal;
            firstUnitPrice = Math.Round(firstUnitPrice * scale, 2, MidpointRounding.AwayFromZero);
            secondUnitPrice = Math.Round(secondUnitPrice * scale, 2, MidpointRounding.AwayFromZero);
        }

        private static string ResolveProductCodeFromCrust(object source)
        {
            object crust = GetPropertyValue(source, "crust", "Crust");
            return ResolveObjectProdNum(crust);
        }

        private static string ResolveObjectProdNum(object source)
        {
            if (source == null)
                return null;

            return ResolveProdNum(
                GetStringProperty(source, "pos_code", "PosCode", "posCode"),
                GetStringProperty(source, "ref_code", "RefCode", "refCode")
            );
        }

        private static object GetPropertyValue(object source, params string[] propertyNames)
        {
            if (source == null || propertyNames == null)
                return null;

            Type type = source.GetType();

            foreach (string propertyName in propertyNames)
            {
                var property = type.GetProperties()
                    .FirstOrDefault(p => string.Equals(
                        p.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase));

                if (property != null)
                    return property.GetValue(source, null);
            }

            return null;
        }

        private static string GetStringProperty(object source, params string[] propertyNames)
        {
            object value = GetPropertyValue(source, propertyNames);
            return value?.ToString();
        }

        private static decimal GetDecimalProperty(object source, params string[] propertyNames)
        {
            object value = GetPropertyValue(source, propertyNames);

            if (value == null)
                return 0;

            if (value is decimal decimalValue)
                return decimalValue;

            if (decimal.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimal result))
            {
                return result;
            }

            if (decimal.TryParse(value.ToString(), out result))
                return result;

            return 0;
        }

        private static int NormalizeQuantity(int? quantity)
        {
            return quantity.HasValue && quantity.Value > 0
                ? quantity.Value
                : 1;
        }

        private static string GetReferenceId(TabOrderRoot root, string uniqueid, string orderid)
        {
            try
            {
                if (root != null && Convert.ToInt32(root.order_number) > 0)
                    return root.order_number.ToString();
            }
            catch
            {
                // Ignore conversion issue and use fallback below.
            }

            if (!string.IsNullOrWhiteSpace(orderid))
                return orderid;

            return uniqueid;
        }

        private static decimal GetParentPrice(CartItem cartItem)
        {
            if (cartItem == null)
                return 0;

            /* Deal base price inside details. */
            if (cartItem.details?.basePrice.HasValue == true
                && cartItem.details.basePrice.Value > 0)
            {
                return cartItem.details.basePrice.Value;
            }

            /* Old payload support. */
            if (cartItem.basePrice.HasValue && cartItem.basePrice.Value > 0)
                return cartItem.basePrice.Value;

            /*
             * Simple item cart price may already include toppings/modifiers.
             * Deduct serialized modifier total so the same amount is not charged twice.
             */
            decimal totalPrice = cartItem.price ?? cartItem.details?.price ?? 0;
            decimal modifierTotal = GetModifierTotal(cartItem);
            decimal parentPrice = totalPrice - modifierTotal;

            if (parentPrice < 0)
                parentPrice = cartItem.details?.price ?? totalPrice;

            return parentPrice;
        }

        private static decimal GetModifierTotal(CartItem cartItem)
        {
            decimal total = 0;

            if (cartItem?.details == null)
                return total;

            /* Deal slot modifiers. */
            if (cartItem.details.slots != null && cartItem.details.slots.Count > 0)
            {
                foreach (var slotRow in cartItem.details.slots)
                {
                    SlotItem slot = slotRow.Value;

                    if (slot == null)
                        continue;

                    total += slot.price ?? 0;
                    total += SumModifierPrices(slot.selected_toppings);

                    if (IsHalfAndHalf(slot))
                    {
                        object firstHalf = GetPropertyValue(slot, "firstHalf", "FirstHalf");
                        object secondHalf = GetPropertyValue(slot, "secondHalf", "SecondHalf");

                        total += SumModifierPrices(
                            GetPropertyValue(firstHalf, "toppings", "selected_toppings")
                        ) * HALF_QUANTITY;

                        total += SumModifierPrices(
                            GetPropertyValue(secondHalf, "toppings", "selected_toppings")
                        ) * HALF_QUANTITY;
                    }
                }

                return total;
            }

            total += SumModifierPrices(cartItem.details.selected_toppings);
            return total;
        }

        private static decimal SumModifierPrices(object modifiers)
        {
            if (!(modifiers is IEnumerable enumerable) || modifiers is string)
                return 0;

            decimal total = 0;

            foreach (object modifier in enumerable)
            {
                if (modifier == null)
                    continue;

                decimal price = GetDecimalProperty(modifier, "price", "Price");
                decimal quantity = GetDecimalProperty(modifier, "quantity", "Quantity");

                if (quantity <= 0)
                    quantity = 1;

                total += price * quantity;
            }

            return total;
        }

        private static string ResolveProdNum(string posCode, string refCode)
        {
            if (!string.IsNullOrWhiteSpace(posCode))
                return posCode.Trim();

            if (!string.IsNullOrWhiteSpace(refCode))
                return refCode.Trim();

            return null;
        }

        private static string ToAmount(decimal amount)
        {
            return amount.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string ToQuantity(decimal quantity)
        {
            return quantity.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static void UpdateTableInfoForTab(string tableNum, string transact)
        {
            if (string.IsNullOrWhiteSpace(tableNum))
                return;

            string query = @"
                UPDATE DBA.TABINFO
                SET TRANSACT = ?
                WHERE TABLENUM = ?";

            ODBCHelper.CURD(
                query,
                new OdbcParameter("TRANSACT", OdbcType.VarChar) { Value = transact },
                new OdbcParameter("TABLENUM", OdbcType.VarChar) { Value = tableNum }
            );
        }

        public static void updatetabel(string uniqueid, string orderid, string transact)
        {
            string query = @"
                UPDATE EOIHQ
                SET STATUS = 1,
                    TRANSACT = @TRANSACT
                WHERE ORDERID = @ORDERID
                  AND UNIQUEID = @UNIQUEID";

            SqlHelper.CURD(
                query,
                new SqlParameter("@TRANSACT", transact),
                new SqlParameter("@ORDERID", orderid),
                new SqlParameter("@UNIQUEID", uniqueid)
            );
        }

        public static void LogXMLToFile(string xmlContent, string orderStatus)
        {
            string currentDate = DateTime.Now.ToString("yyyyMMdd");
            string directoryPath = Path.Combine("Logs/Orders", currentDate);

            Directory.CreateDirectory(directoryPath);

            string orderXml = $"Log_{DateTime.Now:yyyyMMdd_HHmmss}_{orderStatus}.xml";
            string filePath = Path.Combine(directoryPath, orderXml);

            try
            {
                File.WriteAllText(filePath, xmlContent);
                Log.Information("XML logged to file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to log XML to file");
            }
        }

        public static decimal GetNumericPart(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;

            string pattern = @"[-+]?\d*\.?\d+";
            Match match = Regex.Match(input, pattern);

            if (match.Success
                && decimal.TryParse(
                    match.Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal result))
            {
                return result;
            }

            return 0;
        }
    }
}
