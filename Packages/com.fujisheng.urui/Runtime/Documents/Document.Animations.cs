using URUI.Animation;
namespace URUI.Documents
{
    public sealed partial class Document
    {
        AnimationController animations;
        public AnimationController Animations
        {
            get
            {
                CheckThread();
                return animations ??= new AnimationController(this);
            }
        }
    }
}
