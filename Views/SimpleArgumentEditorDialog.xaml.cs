using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace ActiproRoslynPOC.Views
{
    /// <summary>
    /// 简单的参数编辑对话框 - 用于编辑 Dictionary<string, object>
    /// </summary>
    public partial class SimpleArgumentEditorDialog : Window
    {
        public ObservableCollection<KeyValueItem> Parameters { get; set; }
        public bool IsOk { get; private set; }

        public SimpleArgumentEditorDialog()
        {
            InitializeComponent();
            Parameters = new ObservableCollection<KeyValueItem>();
            ParametersDataGrid.ItemsSource = Parameters;
        }

        /// <summary>
        /// 从 Dictionary 加载参数
        /// </summary>
        public void LoadFromDictionary(Dictionary<string, object> dict)
        {
            Parameters.Clear();
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    Parameters.Add(new KeyValueItem
                    {
                        Key = kvp.Key,
                        Value = kvp.Value?.ToString() ?? ""
                    });
                }
            }

            // 添加几个空行方便输入
            if (Parameters.Count == 0)
            {
                Parameters.Add(new KeyValueItem { Key = "", Value = "" });
            }
        }

        /// <summary>
        /// 转换为 Dictionary
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var result = new Dictionary<string, object>();
            foreach (var item in Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Key)))
            {
                // 尝试智能转换类型
                object value = item.Value;

                // 尝试转换为数字
                if (int.TryParse(item.Value, out int intValue))
                {
                    value = intValue;
                }
                else if (double.TryParse(item.Value, out double doubleValue))
                {
                    value = doubleValue;
                }
                else if (bool.TryParse(item.Value, out bool boolValue))
                {
                    value = boolValue;
                }

                result[item.Key] = value;
            }
            return result;
        }

        /// <summary>
        /// 生成 VB.NET 表达式
        /// </summary>
        public string ToVBExpression()
        {
            var dict = ToDictionary();
            if (dict.Count == 0)
            {
                return "Nothing";
            }

            var items = new List<string>();
            foreach (var kvp in dict)
            {
                string valueStr;
                if (kvp.Value is string)
                {
                    valueStr = $"\"{kvp.Value.ToString().Replace("\"", "\"\"")}\"";
                }
                else if (kvp.Value is bool)
                {
                    valueStr = kvp.Value.ToString();
                }
                else
                {
                    valueStr = kvp.Value.ToString();
                }

                items.Add($"{{\"{kvp.Key}\", {valueStr}}}");
            }

            return $"New Dictionary(Of String, Object) From {{{string.Join(", ", items)}}}";
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            IsOk = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsOk = false;
            Close();
        }
    }

    /// <summary>
    /// 键值对数据项
    /// </summary>
    public class KeyValueItem : INotifyPropertyChanged
    {
        private string _key;
        private string _value;

        public string Key
        {
            get => _key;
            set
            {
                _key = value;
                OnPropertyChanged(nameof(Key));
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
