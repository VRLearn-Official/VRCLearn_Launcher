namespace VRCLearn.App.ViewModels;

/// <summary>页面切走时把这一页的数据放掉，视图对象仍留着；切回来由各页原有的刷新路径重新加载。</summary>
public interface IPageViewModel
{
    void Release();
}
