using System.IO;
using System.Windows;
using ActiproSoftware.Text.Implementation; //
using ActiproSoftware.Text.Languages.DotNet.Reflection;
using ActiproSoftware.Text.Languages.DotNet.Reflection.Implementation;
using ActiproSoftware.Text.Parsing.Implementation;
using ActiproSoftware.Text.Parsing;
using ActiproSoftware.Windows.Themes;

namespace ActiproRoslynPOC
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 官方建议：配置后台解析调度器 (解决 UI 卡顿)
            if (AmbientParseRequestDispatcherProvider.Dispatcher == null)
                AmbientParseRequestDispatcherProvider.Dispatcher = new ThreadedParseRequestDispatcher();

            // 官方建议：配置程序集缓存仓库 (解决反射数据加载)
            if (AmbientAssemblyRepositoryProvider.Repository == null)
            {
                string cachePath = Path.Combine(Path.GetTempPath(), "ActiproAssemblyCache");
                AmbientAssemblyRepositoryProvider.Repository = new FileBasedAssemblyRepository(cachePath);
            }

            // 官方推荐：注册并启用内置主题 (例如 Visual Studio 深色)
            // 这会自动处理 GridSplitter 和 TabControl 的边框与背景，消除白色断层
            
            ThemeManager.CurrentTheme = ThemeNames.MetroLight;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 官方建议：退出时清理调度器
            var dispatcher = AmbientParseRequestDispatcherProvider.Dispatcher;
            if (dispatcher != null)
            {
                AmbientParseRequestDispatcherProvider.Dispatcher = null;
                dispatcher.Dispose();
            }
            base.OnExit(e);
        }
    }
}