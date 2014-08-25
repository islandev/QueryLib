using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Configuration;

namespace QueryLib
{
        public class DataDictItem
        {
            // 数据字典的含义
            public string Mean { get; set; }

            // 数据的数字表示，配置中一定要使用数字
            public int Value { get; set; }
        }

        public class DataDict
        {
            private static DataDict _dataDict = new DataDict();
            private XDocument _dataDictDoc = null;

            private DataDict()
            {
                // 此处会抛出没有找到配置节的异常
                string dataDictPath = ConfigurationSettings.AppSettings["DataDictFile"];

                // 会抛出找不到文件的异常
                _dataDictDoc = XDocument.Load(dataDictPath);
            }

            /// <summary>
            /// 获取当前数据字典
            /// </summary>
            public static DataDict CurrentDataDict { get { return _dataDict; } }

            /// <summary>
            /// 获取属于某类的所有数据字典
            /// </summary>
            /// <param name="cataName"></param>
            /// <returns></returns>
            public List<DataDictItem> GetDataDictItems(string cataName)
            {
                IEnumerable<DataDictItem> result = null;
                if (_dataDictDoc != null)
                {
                    var lst = from catalog in _dataDictDoc.Root.Descendants("DataDict")
                              where catalog.Attribute("Name").Value.Equals(cataName)
                              select catalog;

                    result = from item in lst.Descendants("DataItem")
                             select new DataDictItem() { Mean = item.Attribute("Mean").Value, Value = Int32.Parse(item.Attribute("Value").Value) };
                }

                return result.ToList<DataDictItem>();
            }
        }
}
