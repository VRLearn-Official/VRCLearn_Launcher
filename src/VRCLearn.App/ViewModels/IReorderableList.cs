namespace VRCLearn.App.ViewModels;

public interface IReorderableList
{
    void Move(object item, int insertBefore);
}
