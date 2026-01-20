using System;
using System.Activities;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace ActiproRoslynPOC.Views
{
    /// <summary>
    /// 参数编辑对话框
    /// </summary>
    public partial class ArgumentEditorDialog : Window
    {
        public ObservableCollection<ArgumentItem> Arguments { get; set; }
        public new bool DialogResult { get; private set; }

        public ArgumentEditorDialog()
        {
            InitializeComponent();
            Arguments = new ObservableCollection<ArgumentItem>();
            ArgumentsDataGrid.ItemsSource = Arguments;
        }

        /// <summary>
        /// 从 Dictionary 加载参数
        /// </summary>
        public void LoadArguments(Dictionary<string, Argument> arguments)
        {
            Arguments.Clear();
            if (arguments != null)
            {
                foreach (var kvp in arguments)
                {
                    Arguments.Add(new ArgumentItem
                    {
                        Name = kvp.Key,
                        Direction = kvp.Value.Direction.ToString(),
                        ArgumentType = kvp.Value.ArgumentType?.Name ?? "Object",
                        DefaultValue = kvp.Value.Get(null)?.ToString() ?? ""
                    });
                }
            }
        }

        /// <summary>
        /// 获取编辑后的参数
        /// </summary>
        public Dictionary<string, Argument> GetArguments()
        {
            var result = new Dictionary<string, Argument>();
            foreach (var item in Arguments.Where(a => !string.IsNullOrWhiteSpace(a.Name)))
            {
                ArgumentDirection direction = ArgumentDirection.In;
                Enum.TryParse(item.Direction, out direction);

                // 根据类型创建参数
                Type argType = Type.GetType($"System.{item.ArgumentType}") ?? typeof(object);
                var argument = Argument.Create(argType, direction);

                result[item.Name] = argument;
            }
            return result;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// 参数数据项
    /// </summary>
    public class ArgumentItem : INotifyPropertyChanged
    {
        private string _name;
        private string _direction;
        private string _argumentType;
        private string _defaultValue;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                OnPropertyChanged(nameof(Direction));
            }
        }

        public string ArgumentType
        {
            get => _argumentType;
            set
            {
                _argumentType = value;
                OnPropertyChanged(nameof(ArgumentType));
            }
        }

        public string DefaultValue
        {
            get => _defaultValue;
            set
            {
                _defaultValue = value;
                OnPropertyChanged(nameof(DefaultValue));
            }
        }

        public List<string> DirectionOptions => new List<string> { "In", "Out", "InOut" };

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
