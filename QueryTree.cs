using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Xml.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace QueryLib
{
        public class QueryTree
        {
            private static QueryTree _queryTree = new QueryTree();
            private XDocument _xDoc = null;

            private QueryTree()
            {
                // 加载配置路径
                var configPath = ConfigurationSettings.AppSettings["QueryTreeFile"];

                // 读取查询树配置
                _xDoc = XDocument.Load(configPath);
            }

            public static QueryTree CurrentQueryTree { get { return _queryTree; } }

            /// <summary>
            /// 通过配置的查询树，生成查询的代理
            /// </summary>
            /// <typeparam name="TValue">查询对象的类型</typeparam>
            /// <param name="queryName">配置的查询树的名称</param>
            /// <param name="queryParam">参数列表</param>
            /// <returns>查询表达式</returns>
            public Expression<Func<TValue, bool>> MakeQueryDelegate<TValue>(string queryName, IDictionary<string, object> queryParam)
            {
                var root = _xDoc.Root;

                var queryDoc = from sub in root.Descendants("QueryTree")
                               where sub.Attribute("Name").Value.Equals(queryName)
                               select sub;

                // 规定查询树的第一子节点必须是<MultiConcator/>
                var firstBinary = from sub in queryDoc.Descendants("MultiConcator")
                                  select sub;
                if (firstBinary == null)
                    throw new Exception("查询树第一分支必须是<MultiConcator/>");

                // 生成需要查询类型的[参数表达式]
                ParameterExpression param = Expression.Parameter(typeof(TValue));

                // 根据查询树生成查询表达式
                Expression expr = MakeExpression<TValue>(firstBinary.ToList()[0], param, queryParam);

                return Expression.Lambda<Func<TValue, bool>>(expr, param);
            }

            /// <summary>
            /// 根据查询类型生成查询子表达式
            /// </summary>
            /// <typeparam name="TValue">查询对象的类型</typeparam>
            /// <param name="subDoc">查询树子节点</param>
            /// <param name="paramExpr">查询类型的参数表达式</param>
            /// <param name="queryParam">参数列表</param>
            /// <returns>查询子表达式</returns>
            public Expression MakeExpression<TValue>(XElement subDoc, ParameterExpression paramExpr, IDictionary<string, object> queryParam)
            {
                // 获取子节点的[Opt]参数，[Opt]参数为操作方法参数
                string operStr = string.Empty;
                try
                {
                    operStr = subDoc.Attribute("Opt").Value;
                }
                catch (Exception e)
                {
                    throw new Exception("Compare需要<Opt>属性");
                }

                Expression expr = null;
                Expression left = null;
                Expression right = null;

                // 获取此节点的所有下级子节点
                var subLst = from sub in subDoc.Descendants()
                             where sub.Parent == subDoc
                             select sub;

                List<XElement> subElementLst = subLst.ToList<XElement>();

                // 根据子节点类型生成表达式
                if (subDoc.Name.LocalName.Equals("MultiConcator"))
                {
                    // 子节点为子查询树, 进行迭代生成表达式
                    subElementLst.ForEach(item =>
                    {
                        right = MakeExpression<TValue>(item, paramExpr, queryParam);
                        if (expr != null)
                        {
                            expr = BinaryExpression.MakeBinary((ExpressionType)Enum.Parse(typeof(ExpressionType), operStr), expr, right);
                        }
                        else
                        {
                            expr = right;
                        }
                    });
                }
                else if (subElementLst.Count == 1
                    && subElementLst[0].Name.LocalName.Equals("Param")
                    && queryParam.ContainsKey(subElementLst[0].Attribute("Name").Value))
                {
                    // 子节点为[Param]，表示单参数类型，根据[Opt]选项生成表达式
                    var item = subElementLst[0];
                    if (item.Attribute("OwnerProperty") == null)
                    {
                        left = Expression.Property(paramExpr, item.Attribute("Name").Value);
                    }
                    else
                    {
                        var first = Expression.Property(paramExpr, item.Attribute("OwnerProperty").Value);
                        left = Expression.Property(first, item.Attribute("Name").Value);
                    }
                    
                    //判断参数是否存在或者为空
                    if (queryParam.Keys.Contains(item.Attribute("Name").Value) 
                        && queryParam[item.Attribute("Name").Value] != null)
                    {
                        right = Expression.Constant(queryParam[item.Attribute("Name").Value], Type.GetType(item.Attribute("DataType").Value));
                        expr = BinaryExpression.MakeBinary((ExpressionType)Enum.Parse(typeof(ExpressionType), operStr), left, right);
                    }
                    else
                    {
                        expr = Expression.Constant(true, typeof(bool));
                    }
                }
                else if (subElementLst.Count == 1
                    && subElementLst[0].Name.LocalName.Equals("MultiParam")
                    && queryParam.ContainsKey(subElementLst[0].Attribute("Name").Value))
                {
                    // 多条参数[MultiParam]，例如参数[Type="1,2"]为多条参数
                    var item = subElementLst[0];
                    if (item.Attribute("OwnerProperty") == null)
                    {
                        left = Expression.Property(paramExpr, item.Attribute("Name").Value);
                    }
                    else
                    {
                        var first = Expression.Property(paramExpr, item.Attribute("OwnerProperty").Value);
                        left = Expression.Property(first, item.Attribute("Name").Value);
                    }

                    MethodInfo methodInfo = Type.GetType(item.Attribute("DataType").Value).GetMethod("Parse", new Type[] { typeof(string) });
                    List<Expression> expreList = new List<Expression>();
                    string valStr = queryParam[item.Attribute("Name").Value].ToString();
                    var vals = valStr.Split(',');
                    vals.ToList().ForEach(val =>
                    {
                        right = Expression.Constant(methodInfo.Invoke(null, new object[] { val }), Type.GetType(item.Attribute("DataType").Value));
                        expr = BinaryExpression.MakeBinary((ExpressionType)Enum.Parse(typeof(ExpressionType), operStr), left, right);
                        expreList.Add(expr);
                    });

                    expr = null;

                    expreList.ForEach(exprItem =>
                    {
                        if (expr != null)
                        {
                            expr = BinaryExpression.MakeBinary(ExpressionType.Or, expr, exprItem);
                        }
                        else
                        {
                            expr = exprItem;
                        }
                    });
                }
                else if (subElementLst.Count == 1
                    && subElementLst[0].Name.LocalName.Equals("RangeParam")
                    && (queryParam.ContainsKey(subElementLst[0].Attribute("Name").Value + "#0")
                    || (queryParam.ContainsKey(subElementLst[0].Attribute("Name").Value + "#1"))))
                {
                    // 范围参数，参数为一个范围，例如[1,2]
                    // 解析参数
                    // 生成变量表达式
                    var item = subElementLst[0];
                    if (item.Attribute("OwnerProperty") == null)
                    {
                        left = Expression.Property(paramExpr, item.Attribute("Name").Value);
                    }
                    else
                    {
                        var first = Expression.Property(paramExpr, item.Attribute("OwnerProperty").Value);
                        left = Expression.Property(first, item.Attribute("Name").Value);
                    }

                    Expression tempParamExpr = left;

                    // 转换方法
                    MethodInfo methodInfo = Type.GetType(item.Attribute("DataType").Value).GetMethod("Parse", new Type[] { typeof(string) });

                    // 参数名
                    string paramName = subElementLst[0].Attribute("Name").Value;
                    Regex regex = new Regex(paramName + "#[0-1]");
                    var paramList = from param in queryParam.Keys
                                    where regex.IsMatch(param)
                                    select param;

                    // 找到了范围参数
                    if (paramList.ToList().Count == 2)
                    {
                        paramList.ToList().ForEach(paramItem =>
                        {
                            var paramLite = paramItem.Split('#');
                            object queryed = queryParam[paramItem];
                            if (Int32.Parse(paramLite[1]) == 0)
                            {
                                if (queryed != null && !string.IsNullOrEmpty(queryed.ToString()))
                                {
                                    left = Expression.Constant(
                                        methodInfo.Invoke(null, new object[] { queryParam[paramItem].ToString() }),
                                        Type.GetType(item.Attribute("DataType").Value)
                                    );
                                    left = Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, tempParamExpr, left);
                                }
                                else
                                {
                                    left = Expression.Constant(true, typeof(bool));
                                }
                            }
                            else
                            {
                                if (queryed != null && !string.IsNullOrEmpty(queryed.ToString()))
                                {
                                    right = Expression.Constant(
                                        methodInfo.Invoke(null, new object[] { queryParam[paramItem].ToString() }),
                                        Type.GetType(item.Attribute("DataType").Value)
                                    );
                                    right = Expression.MakeBinary(ExpressionType.LessThanOrEqual, tempParamExpr, right);
                                }
                                else
                                {
                                    right = Expression.Constant(true, typeof(bool));
                                }
                            }
                        });
                        expr = Expression.MakeBinary(ExpressionType.And, left, right);
                    }
                }
                else
                {
                    expr = Expression.Constant(true, typeof(bool));
                }

                return expr;
            }
        }
}
