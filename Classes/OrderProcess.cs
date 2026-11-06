using Microsoft.VisualBasic.FileIO;
using PixelPointPOS;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace WeCreatives_EOI.Classes
{
    public static class OrderProcess
    {
        private const int COINIT_APARTMENTTHREADED = 0x2;
        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        public static string PlaceOrderdt(Root root,string uniqueid,string orderid)
        {
            try
            {
                int PRDNUM = 0;
                PRDNUM = Convert.ToInt32(App.FPPRODNUM);
                List<int> PremiumFlavours = new List<int>();

                string query = @"SELECT PRODNUM FROM PremiumFlavours";
                DataTable dt = SqlHelper.SelectRec(query);

                foreach (DataRow dr in dt.Rows)
                {
                    PremiumFlavours.Add(Convert.ToInt32(dr["PRODNUM"]));
                }

                List<ProductModel> products = new List<ProductModel>();

                var itemid = string.Empty;
                #region Deals without dealid
                foreach (var deal in root.Deals.Where(deal => deal.Value.@ref == null))
                {
                    List<Item> DealItems = root.items?.Where(item => item.deal_line?.deal_key == deal.Key).ToList();
                    foreach (Item option in DealItems)
                    {
                        ProductModel deal1 = new ProductModel
                        {
                            prodNum = option.sku_ref,
                            CostEach = GetNumericPart(option.price).ToString(),
                            PriceApplied = "13",
                            itemId = (products.Count() + 1).ToString(),
                            parentId = (products.Count() + 1).ToString(),
                            quantity = option.quantity,
                        };
                        products.Add(deal1);

                        List<Option> dealoptions = new List<Option>();

                        if (option?.options != null)
                            dealoptions = option.options;
                        foreach (Option dealoption in dealoptions)
                        {
                            ProductModel deal3 = new ProductModel
                            {
                                prodNum = dealoption.@ref,
                                CostEach = GetNumericPart(dealoption.price).ToString(),
                                PriceApplied = "13",
                                itemId = (products.Count() + 1).ToString(),
                                parentId = deal1.itemId,
                                quantity = dealoption.Removed == true ? "-1" : option.quantity,
                                AnswerId = dealoption.option_list_name == "Topping" ? "-1" : null
                            };
                            products.Add(deal3);
                        }
                    }
                }
                #endregion

                #region Deals

                foreach (var deal in root.Deals.Where(deal => deal.Value.@ref != null))
                {
                    Deal ddeal = deal.Value;

                    if (ddeal.@ref != "734003464")
                    {
                        string price = root.items?
                            .FirstOrDefault(item => item.deal_line?.deal_key == deal.Key)?
                            .price;

                        ProductModel deal1 = new ProductModel
                        {
                            prodNum = ddeal?.@ref,

                            CostEach = ddeal.@ref == "734004127"
                                ? "0"
                                : GetNumericPart(price).ToString(),

                            PriceApplied = "13",
                            itemId = (products.Count() + 1).ToString(),
                            parentId = (products.Count() + 1).ToString(),
                            quantity = "1",
                        };

                        itemid = deal1.itemId;
                        products.Add(deal1);
                    }

                    List<Item> DealItems = root.items?
                        .Where(item => item.deal_line?.deal_key == deal.Key)
                        .ToList();

                    bool addon = false;
                    var itemidforaddon = string.Empty;

                    if (ddeal.@ref == "734003464")
                    {
                        addon = true;
                        itemidforaddon = (products.Count() + 1).ToString();
                    }

                    var count = 0;

                    foreach (Item dealitem in DealItems)
                    {
                        ProductModel deal2 = new ProductModel
                        {
                            prodNum = dealitem.sku_ref,

                            CostEach =
                                ddeal.@ref == "734004127" ? GetNumericPart(dealitem.price).ToString() :
                                (count > 0 && dealitem.product_name.Contains("Kabab")) ? GetNumericPart(dealitem.price).ToString() :
                                (count > 0 && PremiumFlavours.Contains(Convert.ToInt32(dealitem.sku_ref))) ? GetNumericPart(dealitem.price).ToString() :
                                (addon ? "249" : "0"),

                            itemId = (products.Count() + 1).ToString(),
                            parentId = addon ? itemidforaddon : itemid,
                            quantity = dealitem.quantity,
                        };

                        count++;
                        products.Add(deal2);

                        List<Option> dealoptions = new List<Option>();

                        if (dealitem?.options != null)
                            dealoptions = dealitem.options;

                        foreach (Option dealoption in dealoptions)
                        {
                            ProductModel deal3 = new ProductModel
                            {
                                prodNum = dealoption.@ref,
                                CostEach = GetNumericPart(dealoption.price).ToString(),
                                PriceApplied = "13",
                                itemId = (products.Count() + 1).ToString(),
                                parentId = deal2.itemId,
                                quantity = dealoption.Removed == true ? "-1" : dealitem.quantity,
                                AnswerId = dealoption.option_list_name == "Topping" ? "-1" : null
                            };

                            products.Add(deal3);
                        }

                        count++;
                    }
                }
                #endregion

                #region Simpleitems
                List<Item> sadaitems = root?.items?.Where(item => item.deal_line == null).ToList();
                foreach (Item sada in sadaitems)
                {
                    string side1 = "";
                    string side2 = "";
                    string parenttopping = "";
                    ProductModel deal2 = new ProductModel
                    {
                        prodNum = sada.sku_ref,
                        CostEach = GetNumericPart(sada.price).ToString(),
                        PriceApplied = "13",
                        itemId = (products.Count() + 1).ToString(),
                        parentId = (products.Count() + 1).ToString(),
                        quantity = sada.quantity,
                    };
                    products.Add(deal2);

                    List<Option> sadaoptions = new List<Option>();

                    if (sada?.options != null)
                        sadaoptions = sada.options;
                    foreach (Option sadaoption in sadaoptions)
                    {
                        if (sada.product_name == "Half & Half")
                        {
                            if (sadaoption.option_list_name == "Side 1")
                            {
                                side1 = (products.Count() + 1).ToString();
                                parenttopping = (products.Count() + 1).ToString();
                            }
                            if (sadaoption.option_list_name == "Side 2")
                            {
                                side2 = (products.Count() + 1).ToString();
                                parenttopping = (products.Count() + 1).ToString();
                            }
                            if (sadaoption.option_list_name == "Toppings 1")
                            {
                                parenttopping = side1;
                            }
                            if (sadaoption.option_list_name == "Toppings 2")
                            {
                                parenttopping = side2;
                            }
                            if (sadaoption.option_list_name.Contains("Cheese Portion", StringComparison.OrdinalIgnoreCase))
                            {
                                parenttopping = products.Count().ToString();
                            }
                            else if (sadaoption.option_list_name.Contains("Sauce Portion", StringComparison.OrdinalIgnoreCase))
                            {
                                parenttopping = (products.FirstOrDefault()?.parentId).ToString();
                            }
                            else if (sadaoption.option_list_name.Contains("Sauce", StringComparison.OrdinalIgnoreCase))
                            {
                                parenttopping = (products.FirstOrDefault()?.parentId).ToString();
                            }
                            else if (sadaoption.option_list_name.Contains("Cheese", StringComparison.OrdinalIgnoreCase))
                            {
                                parenttopping = (products.FirstOrDefault()?.parentId).ToString();
                            }

                            ProductModel deal4 = new ProductModel
                            {
                                prodNum = sadaoption.@ref,
                                itemId = (products.Count() + 1).ToString(),
                                parentId = parenttopping,
                                CostEach = GetNumericPart(sadaoption.price).ToString(),
                                PriceApplied = "13",
                                //quantity = sadaoption.option_list_name.Contains("Side")  ? "0.5"  :sadaoption.Removed == true ? "-1" : sadaoption.quantity,
                                quantity =sadaoption.Removed == true ? "-1" : sadaoption.quantity,
                                //AnswerId = sadaoption.option_list_name.Contains("Toppings") ? "-1" : null
                            };
                            products.Add(deal4);
                        }
                        else
                        {
                            var quan = GetNumericPart(sada.quantity);
                            if (quan > 1)
                            {
                                ProductModel deal3 = new ProductModel
                                {
                                    prodNum = sadaoption.@ref,
                                    CostEach = GetNumericPart(sadaoption.price).ToString(),
                                    PriceApplied = "13",
                                    itemId = (products.Count() + 1).ToString(),
                                    parentId = deal2.itemId,
                                    quantity = quan.ToString(),
                                    //AnswerId = sadaoption.option_list_name == "Topping" ? "-1" : null
                                };
                                products.Add(deal3);
                            }
                            else
                            {

                                if (sadaoption.Removed == true)
                                {
                                    ProductModel deal3 = new ProductModel
                                    {
                                        prodNum = sadaoption.@ref,
                                        CostEach = GetNumericPart(sadaoption.price).ToString(),
                                        PriceApplied = "13",
                                        itemId = (products.Count() + 1).ToString(),
                                        parentId = deal2.itemId,
                                        quantity = "-1",
                                        //AnswerId = sadaoption.option_list_name == "Topping" ? "-1" : null
                                    };
                                    products.Add(deal3);
                                }
                                else
                                {
                                    ProductModel deal3 = new ProductModel
                                    {
                                        prodNum = sadaoption.@ref,
                                        CostEach = GetNumericPart(sadaoption.price).ToString(),
                                        PriceApplied = "13",
                                        itemId = (products.Count() + 1).ToString(),
                                        parentId = deal2.itemId,
                                        quantity = sadaoption.quantity,
                                        //AnswerId = sadaoption.option_list_name == "Topping" ? "-1" : null
                                    };
                                    products.Add(deal3);
                                }
                            }
                        }
                    }
                }
                #endregion

                #region Discounts
                if (root.discounts != null)
                {
                    foreach (var discount in root.discounts)
                    {
                        string idd = (products.Count() + 1).ToString();
                        ProductModel deal4 = new ProductModel
                        {
                            couponNum = discount.@ref,
                            itemId = idd,
                            parentId = idd,
                            CostEach = "-" + GetNumericPart(discount.price_off).ToString(),
                            PriceApplied = "13",
                            quantity = "1",
                            // Description = discount.@ref == "734003537" ? "FP Discounts" : "Loyalty"
                            Description = discount.name
                        };
                        products.Add(deal4);
                    }
                }
                #endregion

                #region Delivery Fee
                if (root.charges != null)
                {
                    foreach (var charge in root.charges)
                    {
                        string idd = (products.Count() + 1).ToString();
                        ProductModel charg = new ProductModel
                        {
                            prodNum = charge.@ref,
                            CostEach = GetNumericPart(charge.price).ToString(),
                            PriceApplied = "13",
                            itemId = (products.Count() + 1).ToString(),
                            parentId = (products.Count() + 1).ToString(),
                            quantity = "1",
                        };
                        products.Add(charg);
                    }
                }
                #endregion

                bool comInitialized = false;
                try
                {
                    int comResult = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                    comInitialized = comResult == 0 || comResult == 1;

                    string xml = CreateXML.CreateXMLdt(root, products);
                    PixPosTrans pixPosTrans = new PixPosTrans(); // External interface
                    LogXMLToFile(xml, "IN");

                    string responseXML;

                    try
                    {
                        string saleTypeNum = root.service_type_ref ?? string.Empty;
                        string referenceId;

                        if (saleTypeNum == "1002" || saleTypeNum == "1003")
                        {
                            referenceId = root.id ?? string.Empty;
                        }
                        else
                        {
                            referenceId = $"{root.@ref ?? string.Empty}-{root.collection_code ?? string.Empty}";
                        }

                        string query1 = @"select TRANSACT from dba.posheader where refid=? and opendate =(select max(opendate) from dba.posheader)";
                        OdbcParameter[] parameters = new[] { new OdbcParameter("refid", OdbcType.VarChar) { Value = referenceId } };
                        bool exist = ODBCHelper.ExistRec(query1, parameters);

                        if (exist)
                        {
                            updatetabel(uniqueid, orderid, "11");
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
                        Log.Error(ex, "Error while processing request in PlaceOrderdt.");
                        throw;
                    }

                    XDocument xmlDoc = XDocument.Parse(responseXML);
                    int errorNum = int.TryParse(xmlDoc.Descendants("ErrorNum").FirstOrDefault()?.Value, out int err) ? err : 0;

                    if (errorNum != 0)
                    {
                        Log.Information("PixelPoint POS Error Response: {ResponseXML}", responseXML);
                    }

                    LogXMLToFile(responseXML, "OUT");
                    return responseXML;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "PlaceOrderdt encountered an error.");
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
                            Log.Warning(ex, "Error uninitializing COM in OrderProcess.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Outer exception in OrderProcess.PlaceOrderdt.");
                return ex.Message;
            }
        }

        public static void updatetabel(string uniqueid, string orderid, string transact)
        {
            string query = @"UPDATE EOIHQ SET STATUS = 1, TRANSACT = @TRANSACT WHERE ORDERID = @ORDERID AND UNIQUEID = @UNIQUEID";
            SqlHelper.CURD(query,
                new SqlParameter("@TRANSACT", transact ?? string.Empty),
                new SqlParameter("@ORDERID", orderid ?? string.Empty),
                new SqlParameter("@UNIQUEID", uniqueid ?? string.Empty)
            );
        }

        public static void LogXMLToFile(string xmlContent, string orderStatus)
        {
            try
            {
                string currentDate = DateTime.Now.ToString("yyyyMMdd");
                string directoryPath = Path.Combine("Logs", "Orders", currentDate);
                Directory.CreateDirectory(directoryPath);

                string orderXml = $"Log_{DateTime.Now:yyyyMMdd_HHmmss}_{orderStatus}.xml";
                string filePath = Path.Combine(directoryPath, orderXml);

                File.WriteAllText(filePath, xmlContent);
                Log.Information("XML logged to file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to log XML to file.");
            }
        }

        public static decimal GetNumericPart(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0m;

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

