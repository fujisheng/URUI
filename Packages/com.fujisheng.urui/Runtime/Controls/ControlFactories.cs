using URUI.Documents;

namespace URUI.Controls
{
    internal static class ControlFactories
    {
        internal static void Install(Document document)
        {
            document.RegisterFactory("label", context => new Label(context), eagerInitialize: false);
            document.RegisterFactory("template", context => new Template(context), eagerInitialize: false);
            document.RegisterFactory("img", context => new Image(context), eagerInitialize: false);
            document.RegisterFactory("button", context => new Button(context));
            document.RegisterFactory("input", context => new Toggle(context), "checkbox");
            document.RegisterFactory("input", context => new RadioButton(context), "radio");
            document.RegisterFactory("input", context => new Slider(context), "range");
            document.RegisterFactory("input", context => new IntegerSlider(context), "range");
            document.RegisterFactory("textarea", context => new TextField(context));
            document.RegisterFactory("input", context => new FloatField(context), "number");
            document.RegisterFactory("input", context => new IntegerField(context), "number");
            document.RegisterTag("input:text", typeof(TextField));
            document.RegisterTag("input:password", typeof(TextField));
            document.RegisterFactory("urui-scroll-view", context => new ScrollView(context));
            document.RegisterFactory("select", context => new Dropdown(context));
            document.RegisterFactory("urui-foldout", context => new Foldout(context));
            document.RegisterFactory("urui-progress-bar", context => new ProgressBar(context));
            document.RegisterFactory("urui-list-view", context => new ListView(context));
            document.RegisterFactory("urui-slider", context => new AuthoredSliderSkin(context));
            document.RegisterFactory("urui-select", context => new AuthoredDropdownSkin(context));
            document.RegisterFactory("urui-scroll", context => new AuthoredScrollSkin(context));
        }
    }
}
