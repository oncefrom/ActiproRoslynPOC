using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 工作流参数信息
    /// </summary>
    public class WorkflowParameterInfo
    {
        public string Name { get; set; }
        public Type ParameterType { get; set; }
        public bool HasDefaultValue { get; set; }
        public object DefaultValue { get; set; }
        public bool IsOptional => HasDefaultValue;

        public string TypeDisplayName
        {
            get
            {
                if (ParameterType == typeof(string)) return "string";
                if (ParameterType == typeof(int)) return "int";
                if (ParameterType == typeof(bool)) return "bool";
                if (ParameterType == typeof(double)) return "double";
                if (ParameterType == typeof(decimal)) return "decimal";
                if (ParameterType == typeof(DateTime)) return "DateTime";
                return ParameterType.Name;
            }
        }
    }

    /// <summary>
    /// 工作流签名信息
    /// </summary>
    public class WorkflowSignatureInfo
    {
        public string ClassName { get; set; }
        public string EntryMethodName { get; set; }  // [Workflow] 标记的入口方法名
        public List<WorkflowParameterInfo> InputParameters { get; set; } = new List<WorkflowParameterInfo>();
        public Type ReturnType { get; set; }
        public bool HasCustomParameters => InputParameters.Count > 0;
        public bool HasReturnValue => ReturnType != typeof(void);
        public bool HasWorkflowEntry => !string.IsNullOrEmpty(EntryMethodName);

        public string ReturnTypeDisplayName
        {
            get
            {
                if (ReturnType == null || ReturnType == typeof(void)) return "void";
                if (ReturnType == typeof(string)) return "string";
                if (ReturnType == typeof(int)) return "int";
                if (ReturnType == typeof(bool)) return "bool";

                // 处理元组类型
                if (ReturnType.IsGenericType && ReturnType.Name.StartsWith("ValueTuple"))
                {
                    var genericArgs = ReturnType.GetGenericArguments();
                    var fields = ReturnType.GetFields();
                    var names = new List<string>();

                    for (int i = 0; i < genericArgs.Length; i++)
                    {
                        var typeName = GetSimpleTypeName(genericArgs[i]);
                        var fieldName = fields.Length > i ? fields[i].Name : $"Item{i + 1}";
                        names.Add($"{typeName} {fieldName}");
                    }

                    return $"({string.Join(", ", names)})";
                }

                return ReturnType.Name;
            }
        }

        private string GetSimpleTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(double)) return "double";
            return type.Name;
        }
    }

    /// <summary>
    /// 工作流参数服务
    /// </summary>
    public class WorkflowParameterService
    {
        /// <summary>
        /// 从程序集中获取工作流的签名信息
        /// </summary>
        public WorkflowSignatureInfo GetWorkflowSignature(Assembly assembly, string className)
        {
            var type = assembly.GetTypes().FirstOrDefault(t => t.Name == className);
            if (type == null) return null;

            return GetWorkflowSignature(type);
        }

        /// <summary>
        /// 从类型中获取工作流的签名信息
        /// </summary>
        public WorkflowSignatureInfo GetWorkflowSignature(Type workflowType)
        {
            var info = new WorkflowSignatureInfo
            {
                ClassName = workflowType.Name
            };

            // 查找带有 [Workflow] 特性的方法
            var workflowMethods = workflowType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<Models.WorkflowAttribute>() != null)
                .ToList();

            var executeMethod = workflowMethods.FirstOrDefault();
            if (executeMethod == null)
            {
                // 没有 [Workflow] 特性的方法
                info.ReturnType = typeof(void);
                return info;
            }

            // 保存入口方法名
            info.EntryMethodName = executeMethod.Name;

            // 获取参数信息
            foreach (var param in executeMethod.GetParameters())
            {
                info.InputParameters.Add(new WorkflowParameterInfo
                {
                    Name = param.Name,
                    ParameterType = param.ParameterType,
                    HasDefaultValue = param.HasDefaultValue,
                    DefaultValue = param.HasDefaultValue ? param.DefaultValue : null
                });
            }

            // 获取返回类型
            info.ReturnType = executeMethod.ReturnType;

            return info;
        }

        /// <summary>
        /// 将字符串值转换为指定类型
        /// </summary>
        public object ConvertValue(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (targetType.IsValueType)
                    return Activator.CreateInstance(targetType);
                return null;
            }

            try
            {
                if (targetType == typeof(string)) return value;
                if (targetType == typeof(int)) return int.Parse(value);
                if (targetType == typeof(bool)) return bool.Parse(value);
                if (targetType == typeof(double)) return double.Parse(value);
                if (targetType == typeof(decimal)) return decimal.Parse(value);
                if (targetType == typeof(DateTime)) return DateTime.Parse(value);
                if (targetType == typeof(float)) return float.Parse(value);
                if (targetType == typeof(long)) return long.Parse(value);

                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                if (targetType.IsValueType)
                    return Activator.CreateInstance(targetType);
                return null;
            }
        }
    }
}
