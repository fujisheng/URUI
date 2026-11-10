#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <iostream>

int main()
{
    assert(urui_create(400, 200) == 1);
    auto* context = URUI::GetContext("urui");
    auto* document = context->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>body{width:100px;font-size:14px;}"
        "#width{width:var(--w);--w:50px;} #font{font-size:var(--s);--s:20px;}"
        "</style></head><body><div id='width'></div><div id='font'></div></body></html>", "ui/dynamic-values.html");
    assert(document); document->Show(); context->Update();
    auto* width = document->GetElementById("width");
    auto* font = document->GetElementById("font");
    assert(width->GetBox().GetSize().x == 50 && font->GetComputedValues().font_size() == 20);
    width->SetProperty("--w", "garbage"); context->Update();
    assert(width->GetBox().GetSize().x == 100);
    font->SetProperty("--s", "30px"); context->Update();
    assert(font->GetComputedValues().font_size() == 30);
    width->SetProperty("--w", "75px"); context->Update();
    assert(width->GetBox().GetSize().x == 75);
    font->SetProperty("--s", "garbage"); context->Update();
    assert(font->GetComputedValues().font_size() == 14);
    urui_destroy();
    std::cout << "Dynamic CSS value invalidation contracts passed\n";
}
