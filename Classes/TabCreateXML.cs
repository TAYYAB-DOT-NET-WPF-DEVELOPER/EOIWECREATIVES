using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace WeCreatives_EOI.Classes
{
    public static class TabCreateXML
    {
        public static string TabCreateXMLdt(TabOrderRoot? root, List<ProductModel>? products)
        {
            string requestType = "10";
            string debugMode = "1";

            string saleTypeNum = "1004";
            string Tablnum = "101";
            string extrarefid = "2146437419";
            string referenceId = root?.table_id ?? string.Empty;

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

            XmlElement TableElement = xmlDoc.CreateElement("tablenum");
            TableElement.InnerText = Tablnum;
            transactionElement.AppendChild(TableElement);

            XmlElement extraRefIdElement = xmlDoc.CreateElement("EXTRAREFID");
            extraRefIdElement.InnerText = extrarefid;
            transactionElement.AppendChild(extraRefIdElement);

            XmlElement referenceIdElement = xmlDoc.CreateElement("ReferenceId");
            referenceIdElement.InnerText = referenceId;
            transactionElement.AppendChild(referenceIdElement);

            XmlElement itemsElement = xmlDoc.CreateElement("items");
            transactionElement.AppendChild(itemsElement);

            if (products != null)
            {
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
            }

            return xmlDoc.OuterXml;
        }

        private static decimal GetCartTotal(TabOrderRoot? root)
        {
            decimal total = 0;

            if (root?.cart == null)
                return total;

            foreach (var row in root.cart)
            {
                var item = row.Value;

                if (item == null)
                    continue;

                decimal price = item.price ?? 0;
                int quantity = item.quantity ?? 1;

                total += price * quantity;
            }

            return total;
        }

        public static decimal GetNumericPart(string? input)
        {
            string pattern = @"[-+]?\d*\.?\d+";
            Match match = Regex.Match(input ?? "", pattern);

            if (match.Success && decimal.TryParse(match.Value, out decimal result))
            {
                return result;
            }

            return 0m;
        }
    }
}