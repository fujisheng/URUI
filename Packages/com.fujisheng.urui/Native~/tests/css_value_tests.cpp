#include "urui.h"
#include <URUI/Core.h>
#include <URUI/Core/Animation.h>
#include <cassert>
#include <cmath>
#include <iostream>

int main()
{
    assert(urui_create(400, 200) == 1);
    auto* context = URUI::GetContext("urui");
    auto* document = context->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>body{width:100px;}"
        "#escaped{width:20px;width:\\31 2px;} #quoted{width:21px;width:'12px';}"
        "#custom{--x:foo\\;bar;} #comment{border:2px/**/solid red;} #color{color:r\\65 d;}"
        "</style></head><body><div id='escaped'></div><div id='quoted'></div><div id='custom'></div>"
        "<div id='comment'></div><div id='color'></div></body></html>",
        "ui/values.html");
    assert(document);
    document->Show();
    context->Update();
    URUI::PropertyDictionary image;
    const auto& specification = document->GetPropertySpecification();
    URUI::PropertyDictionary family;
    assert(specification.ParsePropertyDeclaration(family, "font-family", "Arial, 'Times New Roman', serif"));
    assert(!specification.ParsePropertyDeclaration(family, "font-family", "123"));
    assert(!specification.ParsePropertyDeclaration(family, "font-family", "Arial,,serif"));
    assert(!specification.ParsePropertyDeclaration(family, "font-weight", "0"));
    assert(!specification.ParsePropertyDeclaration(family, "font-weight", "-1"));
    assert(!specification.ParsePropertyDeclaration(family, "font-weight", "1001"));
    assert(specification.ParsePropertyDeclaration(family, "font-weight", "1"));
    assert(specification.ParsePropertyDeclaration(family, "font-weight", "1000"));
    URUI::PropertyDictionary colors;
    assert(specification.ParsePropertyDeclaration(colors, "color", "rgba(255, 0, 0, .5)"));
    assert(std::abs(int(colors.GetProperty(URUI::PropertyId::Color)->Get<URUI::Colourb>().alpha) - 128) <= 1);
    assert(specification.ParsePropertyDeclaration(colors, "color", "RGB(255 0 0 / 50%)"));
    assert(std::abs(int(colors.GetProperty(URUI::PropertyId::Color)->Get<URUI::Colourb>().alpha) - 128) <= 1);
    assert(specification.ParsePropertyDeclaration(colors, "color", "rgb(100%, 0%, 0%)"));
    assert(colors.GetProperty(URUI::PropertyId::Color)->Get<URUI::Colourb>() == URUI::Colourb(255, 0, 0));
    assert(!specification.ParsePropertyDeclaration(colors, "color", "rgba(255, 0, 0, garbage)"));
    assert(!specification.ParsePropertyDeclaration(colors, "color", "rgba(255, 0, 0, 1px)"));
    assert(!specification.ParsePropertyDeclaration(colors, "color", "rgb(255+0+0)"));
    assert(!specification.ParsePropertyDeclaration(colors, "color", "rgb(255,0,0)garbage"));
    URUI::PropertyDictionary timing;
    assert(specification.ParsePropertyDeclaration(timing, "animation", "1s ease"));
    assert(timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>().empty());
    assert(specification.ParsePropertyDeclaration(timing, "transition", "1s ease"));
    assert(timing.GetProperty(URUI::PropertyId::Transition)->value.GetReference<URUI::TransitionList>().all);
    assert(specification.ParsePropertyDeclaration(timing, "animation", "none garbage"));
    assert(timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>().size() == 1);
    assert(timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>()[0].name ==
           "garbage");
    assert(specification.ParsePropertyDeclaration(timing, "animation", "flash none"));
    assert(specification.ParsePropertyDeclaration(timing, "animation", "none none"));
    assert(timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>().empty());
    assert(!specification.ParsePropertyDeclaration(timing, "animation", "none none garbage"));
    assert(!specification.ParsePropertyDeclaration(timing, "animation", "none none none"));
    assert(specification.ParsePropertyDeclaration(timing, "animation", "none 1s ease"));
    assert(timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>().empty());
    assert(specification.ParsePropertyDeclaration(timing, "animation", "none, flash 1s"));
    assert(timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>().size() == 1);
    assert(specification.ParsePropertyDeclaration(timing, "transition", "opacity 0s"));
    assert(!specification.ParsePropertyDeclaration(timing, "transition", "none garbage"));
    assert(!specification.ParsePropertyDeclaration(timing, "transition", "none, opacity 1s"));
    assert(specification.ParsePropertyDeclaration(timing, "transition", "none 1s"));
    assert(timing.GetProperty(URUI::PropertyId::Transition)->value.GetReference<URUI::TransitionList>().none);
    assert(!specification.ParsePropertyDeclaration(timing, "transition", "none -1s"));
    assert(!specification.ParsePropertyDeclaration(timing, "transition", "none none"));
    for (const char* easing : {"back-out", "bounce-in-out", "elastic-out", "quadratic-in", "linear-in-out"})
    {
        assert(!specification.ParsePropertyDeclaration(timing, "transition", URUI::String("opacity 1s ") + easing));
    }
    assert(!specification.ParsePropertyDeclaration(timing, "animation", "flash 1s bounce-out"));
    assert(!specification.ParsePropertyDeclaration(timing, "animation", "flash 1s ease linear"));
    assert(!specification.ParsePropertyDeclaration(timing, "transition", "opacity 1s ease linear"));
    assert(specification.ParsePropertyDeclaration(timing, "animation", "bounce-out 1s"));
    const auto& named = timing.GetProperty(URUI::PropertyId::Animation)->value.GetReference<URUI::AnimationList>();
    assert(named.size() == 1 && named[0].name == "bounce-out" && std::abs(named[0].tween(.5f) - .802403f) < .00005f);
    assert(specification.ParsePropertyDeclaration(timing, "transition", "opacity 1s EASE"));
    const auto& transitions =
        timing.GetProperty(URUI::PropertyId::Transition)->value.GetReference<URUI::TransitionList>();
    assert(std::abs(transitions.transitions[0].tween(.5f) - .802403f) < .00005f);
    assert(specification.ParsePropertyDeclaration(timing, "transition", "opacity 1s linear(0, .5 20%, 1)"));
    assert(specification.ParsePropertyDeclaration(image, "mask-image", "url(mask.png)"));
    assert(specification.ParsePropertyDeclaration(image, "mask-image", "url(mask\\ with\\ spaces.png)"));
    assert(specification.ParsePropertyDeclaration(image, "mask-image", "url(trailing\\ )"));
    assert(specification.ParsePropertyDeclaration(image, "mask-image", "url(\"mask with spaces.png\"), none"));
    assert(specification.ParsePropertyDeclaration(image, "mask-image", "none"));
    assert(!specification.ParsePropertyDeclaration(image, "mask-image", "image('mask.png')"));
    assert(!specification.ParsePropertyDeclaration(image, "mask-image", "url('mask.png') cover"));
    assert(!specification.ParsePropertyDeclaration(image, "mask-image", "url(mask with spaces.png)"));
    assert(!specification.ParsePropertyDeclaration(image, "mask-image", "url(mask.png"));
    assert(!specification.ParsePropertyDeclaration(image, "mask-image", "shader(game-mask)"));
    assert(specification.ParsePropertyDeclaration(image, "--urui-mask-image", "shader(game-mask)"));
    auto* masked = document->GetElementById("custom");
    assert(masked->SetProperty("--urui-mask-image", "shader(game-mask)"));
    context->Update();
    assert(masked->GetComputedValues().has_mask_image());
    assert(masked->SetProperty("mask-image", "none"));
    context->Update();
    assert(masked->GetComputedValues().has_mask_image());
    assert(masked->SetProperty("--urui-mask-image", "none"));
    context->Update();
    assert(!masked->GetComputedValues().has_mask_image());
    assert(document->GetElementById("escaped")->GetBox().GetSize().x == 20);
    assert(document->GetElementById("quoted")->GetBox().GetSize().x == 21);
    assert(document->GetElementById("custom")->GetProperty("--x"));
    assert(document->GetElementById("comment")->GetComputedValues().border_top_width() == 2);
    assert(document->GetElementById("color")->GetComputedValues().color() == URUI::Colourb(255, 0, 0));
    urui_destroy();
    std::cout << "CSS token identity contracts passed\n";
}
