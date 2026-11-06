using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace WeCreatives_EOI.Classes
{
    public static class CreateXML
    {
        public static string CreateXMLdt(Root? root, List<ProductModel>? products)
        {
            if (root == null)
            {
                Log.Warning("CreateXMLdt called with null Root object.");
                return string.Empty;
            }

            products ??= new List<ProductModel>();

            string requestType = "10";
            string debugMode = "1";
            string saleTypeNum = root.service_type_ref ?? string.Empty;
            string extrarefid = "2146437419";
            string referenceId;

            if (saleTypeNum == "1002" || saleTypeNum == "1003")
            {
                referenceId = root.id ?? string.Empty;
            }
            else
            {
                referenceId = $"{root.@ref ?? string.Empty}-{root.collection_code ?? string.Empty}";
            }

            XmlDocument xmlDoc = new XmlDocument();
            XmlElement rootElement = xmlDoc.CreateElement("Request");
            xmlDoc.AppendChild(rootElement);

            XmlElement requestTypeElement = xmlDoc.CreateElement("RequestType");
            requestTypeElement.InnerText = requestType;
            rootElement.AppendChild(requestTypeElement);

            XmlElement debugModeElement = xmlDoc.CreateElement("DebugMode");
            debugModeElement.InnerText = debugMode;
            rootElement.AppendChild(debugModeElement);

            XmlElement transactionElement = xmlDoc.CreateElement("Transaction");
            rootElement.AppendChild(transactionElement);

            XmlElement saleTypeNumElement = xmlDoc.CreateElement("SaleTypeNum");
            saleTypeNumElement.InnerText = saleTypeNum;
            transactionElement.AppendChild(saleTypeNumElement);

            XmlElement WHOSTARTELEMENT = xmlDoc.CreateElement("EXTRAREFID");
            WHOSTARTELEMENT.InnerText = extrarefid;
            transactionElement.AppendChild(WHOSTARTELEMENT);

            XmlElement referenceIdElement = xmlDoc.CreateElement("ReferenceId");
            referenceIdElement.InnerText = referenceId;
            transactionElement.AppendChild(referenceIdElement);

            #region Payments
            if (root.payments != null && root.payments.Count > 0)
            {
                XmlElement paymentsElement = xmlDoc.CreateElement("Payments");
                transactionElement.AppendChild(paymentsElement);

                foreach (var payment in root.payments)
                {
                    if (payment == null) continue;

                    string paymentRef = (payment.@ref ?? string.Empty).Trim();
                    if (paymentRef == "1001") continue;

                    if (root.service_type_ref == "734005201" || payment.name == "alfalah" || payment.name == "Paid online" || !string.IsNullOrWhiteSpace(paymentRef))
                    {
                        XmlElement paymentElement = xmlDoc.CreateElement("Payment");
                        paymentsElement.AppendChild(paymentElement);

                        XmlElement methodNumberElement = xmlDoc.CreateElement("MethodNumber");
                        methodNumberElement.InnerText = paymentRef;
                        paymentElement.AppendChild(methodNumberElement);

                        XmlElement tenderElement = xmlDoc.CreateElement("Tender");
                        tenderElement.InnerText = GetNumericPart(payment.amount).ToString();
                        paymentElement.AppendChild(tenderElement);

                        XmlElement authTypeElement = xmlDoc.CreateElement("AuthType");
                        authTypeElement.InnerText = "114";
                        paymentElement.AppendChild(authTypeElement);
                    }
                }
            }
            #endregion

            XmlElement itemsElement = xmlDoc.CreateElement("items");
            transactionElement.AppendChild(itemsElement);

            foreach (var product in products)
            {
                if (product == null) continue;

                XmlElement itemElement = xmlDoc.CreateElement("item");
                itemsElement.AppendChild(itemElement);

                foreach (var property in product.GetType().GetProperties())
                {
                    var value = property.GetValue(product);
                    if (value != null)
                    {
                        XmlElement propertyElement = xmlDoc.CreateElement(property.Name);
                        propertyElement.InnerText = value.ToString() ?? string.Empty;
                        itemElement.AppendChild(propertyElement);
                    }
                }
            }

            XmlElement memberElement = xmlDoc.CreateElement("Member");
            rootElement.AppendChild(memberElement);

            bool matchFound = false;

            if (root.customer?.custom_fields?.pixel_point?.member_codes != null)
            {
                XmlElement SaveMemberInfoToDBElement = xmlDoc.CreateElement("SaveMemberInfoToDB");
                SaveMemberInfoToDBElement.InnerText = "1";
                memberElement.AppendChild(SaveMemberInfoToDBElement);

                foreach (var code in root.customer.custom_fields.pixel_point.member_codes)
                {
                    string value = code.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    string query = @"SELECT MEMCODE FROM DBA.MEMBER WHERE MEMCODE = ?";
                    OdbcParameter[] parameters = new[] { new OdbcParameter("MEMCODE", OdbcType.VarChar) { Value = value } };
                    DataTable dt = ODBCHelper.SelectRec(query, parameters);

                    if (dt != null && dt.Rows.Count > 0)
                    {
                        foreach (DataRow dr in dt.Rows)
                        {
                            string memCode = dr["MEMCODE"]?.ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(memCode))
                            {
                                XmlElement MemCodeElement = xmlDoc.CreateElement("MemCode");
                                MemCodeElement.InnerText = value;
                                memberElement.AppendChild(MemCodeElement);

                                XmlElement UPDATEDDirectionsElement = xmlDoc.CreateElement("Directions");
                                UPDATEDDirectionsElement.InnerText = root.customer.delivery_notes ?? string.Empty;
                                memberElement.AppendChild(UPDATEDDirectionsElement);

                                XmlElement UPDATEDCountryElement = xmlDoc.CreateElement("Country");
                                UPDATEDCountryElement.InnerText = root.customer.country ?? string.Empty;
                                memberElement.AppendChild(UPDATEDCountryElement);
                                matchFound = true;
                                break;
                            }
                        }
                    }

                    if (matchFound)
                    {
                        break;
                    }
                }
            }

            if (root.customer != null)
            {
                XmlElement FirstNameElement = xmlDoc.CreateElement("FirstName");
                FirstNameElement.InnerText = root.customer.first_name ?? string.Empty;
                memberElement.AppendChild(FirstNameElement);

                XmlElement LastNameElement = xmlDoc.CreateElement("LastName");
                LastNameElement.InnerText = root.customer.last_name ?? string.Empty;
                memberElement.AppendChild(LastNameElement);

                XmlElement HomeTeleElement = xmlDoc.CreateElement("HomeTele");
                HomeTeleElement.InnerText = root.customer.phone ?? string.Empty;
                memberElement.AppendChild(HomeTeleElement);

                XmlElement HOMEADDRESS = xmlDoc.CreateElement("Address1");
                HOMEADDRESS.InnerText = root.customer.address_1 ?? string.Empty;
                memberElement.AppendChild(HOMEADDRESS);

                XmlElement HOMEADDRESS2 = xmlDoc.CreateElement("Address2");
                HOMEADDRESS2.InnerText = root.customer.address_2 ?? string.Empty;
                memberElement.AppendChild(HOMEADDRESS2);

                XmlElement City = xmlDoc.CreateElement("City");
                City.InnerText = root.customer.city ?? string.Empty;
                memberElement.AppendChild(City);

                XmlElement EmailElement = xmlDoc.CreateElement("Email");
                EmailElement.InnerText = root.customer.email ?? string.Empty;
                memberElement.AppendChild(EmailElement);

                XmlElement DirectionsElement = xmlDoc.CreateElement("Directions");
                DirectionsElement.InnerText = root.customer.delivery_notes ?? string.Empty;
                memberElement.AppendChild(DirectionsElement);

                XmlElement CountryElement = xmlDoc.CreateElement("Country");
                CountryElement.InnerText = root.customer.country ?? string.Empty;
                memberElement.AppendChild(CountryElement);
            }

            return xmlDoc.OuterXml;
        }

        /// <summary>
        /// Creates PixelPoint POS XML from a Call Center order JSON string.
        /// </summary>
        /// <param name="jsonString">JSON payload of Call Center order</param>
        /// <returns>PixelPoint POS XML string</returns>
        public static string CreateCallCenterXMLdt(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                Log.Warning("CreateCallCenterXMLdt called with empty JSON string.");
                return string.Empty;
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
                };

                CallCenterOrderRoot? root = JsonSerializer.Deserialize<CallCenterOrderRoot>(jsonString, options);
                return CreateCallCenterXMLdt(root, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize Call Center JSON in CreateCallCenterXMLdt.");
                return string.Empty;
            }
        }

        /// <summary>
        /// Creates PixelPoint POS XML from a CallCenterOrderRoot object.
        /// </summary>
        /// <param name="root">CallCenterOrderRoot object</param>
        /// <param name="products">Optional custom list of ProductModel items</param>
        /// <returns>PixelPoint POS XML string</returns>
        public static string CreateCallCenterXMLdt(CallCenterOrderRoot? root, List<ProductModel>? products = null)
        {
            if (root == null)
            {
                Log.Warning("CreateCallCenterXMLdt called with null CallCenterOrderRoot object.");
                return string.Empty;
            }

            string requestType = "10";
            string debugMode = "1";
            string saleTypeNum = root.sale_type_index?.ToString() ?? "1000";
            if (string.IsNullOrWhiteSpace(saleTypeNum))
            {
                saleTypeNum = "1000";
            }
            string extrarefid = "2146437419";
            string referenceId = root.order_number ?? string.Empty;

            XmlDocument xmlDoc = new XmlDocument();
            XmlElement rootElement = xmlDoc.CreateElement("Request");
            xmlDoc.AppendChild(rootElement);

            XmlElement requestTypeElement = xmlDoc.CreateElement("RequestType");
            requestTypeElement.InnerText = requestType;
            rootElement.AppendChild(requestTypeElement);

            XmlElement debugModeElement = xmlDoc.CreateElement("DebugMode");
            debugModeElement.InnerText = debugMode;
            rootElement.AppendChild(debugModeElement);

            XmlElement transactionElement = xmlDoc.CreateElement("Transaction");
            rootElement.AppendChild(transactionElement);

            XmlElement saleTypeNumElement = xmlDoc.CreateElement("SaleTypeNum");
            saleTypeNumElement.InnerText = saleTypeNum;
            transactionElement.AppendChild(saleTypeNumElement);

            XmlElement extraRefIdElement = xmlDoc.CreateElement("EXTRAREFID");
            extraRefIdElement.InnerText = extrarefid;
            transactionElement.AppendChild(extraRefIdElement);

            XmlElement referenceIdElement = xmlDoc.CreateElement("ReferenceId");
            referenceIdElement.InnerText = referenceId;
            transactionElement.AppendChild(referenceIdElement);

            #region Payments
            string paymentMethod = root.payment_method_index?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(paymentMethod))
            {
                XmlElement paymentsElement = xmlDoc.CreateElement("Payments");
                transactionElement.AppendChild(paymentsElement);

                XmlElement paymentElement = xmlDoc.CreateElement("Payment");
                paymentsElement.AppendChild(paymentElement);

                XmlElement methodNumberElement = xmlDoc.CreateElement("MethodNumber");
                methodNumberElement.InnerText = paymentMethod;
                paymentElement.AppendChild(methodNumberElement);

                XmlElement tenderElement = xmlDoc.CreateElement("Tender");
                decimal tenderAmount = root.total_amount > 0 ? root.total_amount : root.sub_total;
                tenderElement.InnerText = tenderAmount.ToString(CultureInfo.InvariantCulture);
                paymentElement.AppendChild(tenderElement);

                XmlElement authTypeElement = xmlDoc.CreateElement("AuthType");
                authTypeElement.InnerText = "114";
                paymentElement.AppendChild(authTypeElement);
            }
            #endregion

            #region Items Mapping
            if (products == null || products.Count == 0)
            {
                products = new List<ProductModel>();
                if (root.items != null && root.items.Count > 0)
                {
                    int currentId = 0;
                    ProcessCallCenterItems(root.items, products, ref currentId, null);
                }

                if (root.delivery_charges > 0)
                {
                    int currentId = products.Count + 1;
                    products.Add(new ProductModel
                    {
                        prodNum = "734005201",
                        itemId = currentId.ToString(),
                        parentId = currentId.ToString(),
                        quantity = "1",
                        CostEach = root.delivery_charges.ToString(CultureInfo.InvariantCulture),
                        PriceApplied = "13",
                        Description = "Delivery Charges"
                    });
                }

                if (root.discount_amount > 0)
                {
                    int currentId = products.Count + 1;
                    products.Add(new ProductModel
                    {
                        couponNum = "734003537",
                        itemId = currentId.ToString(),
                        parentId = currentId.ToString(),
                        quantity = "1",
                        CostEach = "-" + root.discount_amount.ToString(CultureInfo.InvariantCulture),
                        PriceApplied = "13",
                        Description = "CallCenter Discount"
                    });
                }
            }

            XmlElement itemsElement = xmlDoc.CreateElement("items");
            transactionElement.AppendChild(itemsElement);

            foreach (var product in products)
            {
                if (product == null) continue;

                XmlElement itemElement = xmlDoc.CreateElement("item");
                itemsElement.AppendChild(itemElement);

                foreach (var property in product.GetType().GetProperties())
                {
                    var value = property.GetValue(product);
                    if (value != null)
                    {
                        XmlElement propertyElement = xmlDoc.CreateElement(property.Name);
                        propertyElement.InnerText = value.ToString() ?? string.Empty;
                        itemElement.AppendChild(propertyElement);
                    }
                }
            }
            #endregion

            #region Member
            XmlElement memberElement = xmlDoc.CreateElement("Member");
            rootElement.AppendChild(memberElement);

            string phone = root.customer_info?.phone ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(phone))
            {
                try
                {
                    string query = @"SELECT MEMCODE FROM DBA.MEMBER WHERE HOMETELE = ? OR CELLTELE = ? OR MEMCODE = ?";
                    OdbcParameter[] parameters = new[]
                    {
                        new OdbcParameter("HOMETELE", OdbcType.VarChar) { Value = phone },
                        new OdbcParameter("CELLTELE", OdbcType.VarChar) { Value = phone },
                        new OdbcParameter("MEMCODE", OdbcType.VarChar) { Value = phone }
                    };
                    DataTable dt = ODBCHelper.SelectRec(query, parameters);

                    if (dt != null && dt.Rows.Count > 0)
                    {
                        string memCode = dt.Rows[0]["MEMCODE"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(memCode))
                        {
                            XmlElement memCodeElement = xmlDoc.CreateElement("MemCode");
                            memCodeElement.InnerText = memCode;
                            memberElement.AppendChild(memCodeElement);

                            XmlElement saveMemberInfoToDBElement = xmlDoc.CreateElement("SaveMemberInfoToDB");
                            saveMemberInfoToDBElement.InnerText = "1";
                            memberElement.AppendChild(saveMemberInfoToDBElement);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error checking DBA.MEMBER for Call Center customer phone {Phone}.", phone);
                }
            }

            if (root.customer_info != null || root.customer_address != null)
            {
                XmlElement firstNameElement = xmlDoc.CreateElement("FirstName");
                firstNameElement.InnerText = root.customer_info?.first_name ?? string.Empty;
                memberElement.AppendChild(firstNameElement);

                XmlElement lastNameElement = xmlDoc.CreateElement("LastName");
                lastNameElement.InnerText = root.customer_info?.last_name ?? string.Empty;
                memberElement.AppendChild(lastNameElement);

                XmlElement homeTeleElement = xmlDoc.CreateElement("HomeTele");
                homeTeleElement.InnerText = root.customer_info?.phone ?? string.Empty;
                memberElement.AppendChild(homeTeleElement);

                XmlElement address1Element = xmlDoc.CreateElement("Address1");
                address1Element.InnerText = root.customer_address?.address_line1 ?? string.Empty;
                memberElement.AppendChild(address1Element);

                XmlElement address2Element = xmlDoc.CreateElement("Address2");
                address2Element.InnerText = root.customer_address?.address_line2 ?? string.Empty;
                memberElement.AppendChild(address2Element);

                XmlElement cityElement = xmlDoc.CreateElement("City");
                cityElement.InnerText = root.customer_address?.city ?? string.Empty;
                memberElement.AppendChild(cityElement);

                XmlElement emailElement = xmlDoc.CreateElement("Email");
                emailElement.InnerText = root.customer_info?.email ?? string.Empty;
                memberElement.AppendChild(emailElement);

                string directions = root.notes;
                if (string.IsNullOrWhiteSpace(directions))
                {
                    directions = string.Join(", ", new[] { root.customer_address?.address_line3, root.customer_address?.nearby_landmark }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                }

                XmlElement directionsElement = xmlDoc.CreateElement("Directions");
                directionsElement.InnerText = directions ?? string.Empty;
                memberElement.AppendChild(directionsElement);

                XmlElement countryElement = xmlDoc.CreateElement("Country");
                countryElement.InnerText = root.customer_address?.province ?? string.Empty;
                memberElement.AppendChild(countryElement);
            }
            #endregion

            return xmlDoc.OuterXml;
        }

        private static void ProcessCallCenterItems(
            List<CallCenterOrderItem>? items,
            List<ProductModel> products,
            ref int currentId,
            string? parentId = null)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item == null) continue;

                string prodCode = !string.IsNullOrWhiteSpace(item.pos_code) ? item.pos_code : (item.ref_code ?? string.Empty);

                currentId++;
                string myItemId = currentId.ToString();
                string myParentId = parentId ?? myItemId;

                if (!string.IsNullOrWhiteSpace(prodCode))
                {
                    decimal unitPrice = item.unit_price > 0
                        ? item.unit_price
                        : (item.quantity > 0 ? item.total_price / item.quantity : item.total_price);

                    ProductModel pm = new ProductModel
                    {
                        prodNum = prodCode,
                        itemId = myItemId,
                        parentId = myParentId,
                        quantity = (item.quantity != 0 ? item.quantity : 1).ToString(CultureInfo.InvariantCulture),
                        CostEach = unitPrice.ToString(CultureInfo.InvariantCulture),
                        PriceApplied = "13",
                        Description = item.item_name
                    };

                    products.Add(pm);
                }
                else
                {
                    myItemId = myParentId;
                    currentId--;
                }

                if (item.child_items != null && item.child_items.Count > 0)
                {
                    ProcessCallCenterItems(item.child_items, products, ref currentId, myItemId);
                }

                if (item.modifiers != null && item.modifiers.Count > 0)
                {
                    ProcessCallCenterModifiers(item.modifiers, products, ref currentId, myItemId);
                }
            }
        }

        private static void ProcessCallCenterModifiers(
            List<CallCenterModifier>? modifiers,
            List<ProductModel> products,
            ref int currentId,
            string parentId)
        {
            if (modifiers == null) return;

            foreach (var mod in modifiers)
            {
                if (mod == null) continue;

                string prodCode = !string.IsNullOrWhiteSpace(mod.pos_code) ? mod.pos_code : (mod.ref_code ?? string.Empty);

                currentId++;
                string myItemId = currentId.ToString();
                string myParentId = parentId;

                if (!string.IsNullOrWhiteSpace(prodCode))
                {
                    decimal unitPrice = mod.unit_price > 0
                        ? mod.unit_price
                        : (mod.quantity > 0 && mod.total_price > 0 ? mod.total_price / mod.quantity : mod.total_price);

                    ProductModel pm = new ProductModel
                    {
                        prodNum = prodCode,
                        itemId = myItemId,
                        parentId = myParentId,
                        quantity = (mod.quantity != 0 ? mod.quantity : 1).ToString(CultureInfo.InvariantCulture),
                        CostEach = unitPrice.ToString(CultureInfo.InvariantCulture),
                        PriceApplied = "13",
                        Description = mod.modifier_name
                    };

                    products.Add(pm);
                }
                else
                {
                    myItemId = myParentId;
                    currentId--;
                }

                if (mod.child_items != null && mod.child_items.Count > 0)
                {
                    ProcessCallCenterItems(mod.child_items, products, ref currentId, myItemId);
                }

                if (mod.modifiers != null && mod.modifiers.Count > 0)
                {
                    ProcessCallCenterModifiers(mod.modifiers, products, ref currentId, myItemId);
                }
            }
        }

        public static decimal GetNumericPart(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0m;

            string pattern = @"[-+]?\d*\.?\d+";
            Match match = Regex.Match(input, pattern);

            if (match.Success && decimal.TryParse(match.Value, out decimal result))
            {
                return result;
            }

            return 0m;
        }
    }
}
