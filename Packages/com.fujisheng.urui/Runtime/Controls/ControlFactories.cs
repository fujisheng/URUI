using URUI.Documents;

namespace URUI.Controls
{
    internal static class ControlFactories
    {
        internal static void Install(Document document)
        {
            document.RegisterFactory("label", context => new Label(context));
            document.RegisterFactory("img", context => new Image(context));
            document.RegisterFactory("button", context => new Button(context));
            document.RegisterFactory("input", context => new Toggle(context), "checkbox");
            document.RegisterFactory("input", context => new RadioButton(context), "radio");
            document.RegisterFactory("input", context => new Slider(context), "range");
            document.RegisterFactory("input", context => new IntegerSlider(context), "range");
            document.RegisterFactory("textarea", context => new TextField(context));
            document.RegisterFactory("input", context => new IntegerField(context), "number");
            document.RegisterFactory("input", context => new FloatField(context), "text");
            document.RegisterTag("input:text", typeof(TextField));
            document.RegisterTag("input:password", typeof(TextField));
            document.RegisterFactory("scroll-view", context => new ScrollView(context));
            document.RegisterFactory("select", context => new Dropdown(context));
            document.RegisterFactory("foldout", context => new Foldout(context));
            document.RegisterFactory("progress-bar", context => new ProgressBar(context));
            document.RegisterFactory("list-view", context => new ListView(context));
        }
    }
}
