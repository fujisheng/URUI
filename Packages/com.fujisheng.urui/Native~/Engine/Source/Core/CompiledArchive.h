#pragma once
#include "CompiledDocument.h"
#include "StyleSheetNode.h"
#include <URUI/Core/Animation.h>
#include <URUI/Core/DecorationTypes.h>
#include <URUI/Core/Decorator.h>
#include <URUI/Core/Filter.h>
#include <URUI/Core/FontEffect.h>
#include <URUI/Core/FontEffectInstancer.h>
#include <URUI/Core/PropertyDefinition.h>
#include <URUI/Core/PropertySpecification.h>
#include <URUI/Core/StyleSheet.h>
#include <URUI/Core/StyleSheetSpecification.h>
#include <URUI/Core/Transform.h>
#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>

namespace URUI
{
    class DocumentHeader;
    // 只序列化显式字段；指针、缓存和平台布局不进入文件。
    class CompiledArchive
    {
    public:
        explicit CompiledArchive(Vector<byte>& output) : writing(true), output(&output)
        {
        }
        CompiledArchive(const byte* data, size_t size, const PropertySpecification* rebind = nullptr)
            : data(data), remaining(size), rebind(rebind)
        {
        }
        bool Complete() const
        {
            return remaining == 0;
        }
        size_t Remaining() const
        {
            return remaining;
        }
        static bool CompileStyles(const DocumentHeader& header, FileInterface* files, CompiledDocument& compiled,
                                  Vector<Diagnostic>& diagnostics);
        static constexpr size_t MaxBytes = 64u * 1024u * 1024u;
        static constexpr uint32_t MaxItems = 1000000;

        void Value(uint64_t& value)
        {
            if (writing)
            {
                for (int i = 0; i < 8; i++)
                {
                    Put(byte(value >> (i * 8)));
                }
            }
            else
            {
                Require(8);
                value = 0;
                for (int i = 0; i < 8; i++)
                {
                    value |= uint64_t(Take()) << (i * 8);
                }
            }
        }
        template <class T, std::enable_if_t<std::is_integral_v<T> || std::is_enum_v<T>, int> = 0> void Value(T& value)
        {
            static_assert(sizeof(T) <= 4);
            uint32_t raw = writing ? uint32_t(value) : 0;
            if (writing)
            {
                for (int i = 0; i < 4; i++)
                {
                    Put(byte(raw >> (i * 8)));
                }
            }
            else
            {
                Require(4);
                for (int i = 0; i < 4; i++)
                {
                    raw |= uint32_t(Take()) << (i * 8);
                }
                value = T(raw);
            }
        }
        void Value(int64_t& value)
        {
            uint64_t raw = uint64_t(value);
            Value(raw);
            if (!writing)
            {
                value = int64_t(raw);
            }
        }
        void Value(float& value)
        {
            uint32_t raw = 0;
            if (writing)
            {
                std::memcpy(&raw, &value, 4);
            }
            Value(raw);
            if (!writing)
            {
                std::memcpy(&value, &raw, 4);
            }
        }
        void Value(double& value)
        {
            uint64_t raw = 0;
            if (writing)
            {
                std::memcpy(&raw, &value, 8);
            }
            Value(raw);
            if (!writing)
            {
                std::memcpy(&value, &raw, 8);
            }
        }
        void Value(String& value)
        {
            uint32_t size = writing ? Checked(value.size()) : 0;
            Value(size);
            if (writing)
            {
                for (unsigned char c : value)
                {
                    Put(c);
                }
            }
            else
            {
                Require(size);
                value.assign(reinterpret_cast<const char*>(data), size);
                data += size;
                remaining -= size;
            }
        }
        template <class T, class A> void Value(std::vector<T, A>& values)
        {
            uint32_t size = writing ? Checked(values.size()) : 0;
            Value(size);
            Count(size);
            if (!writing)
            {
                values.resize(size);
            }
            for (auto& value : values)
            {
                Value(value);
            }
        }
        template <class T, size_t N> void Value(std::array<T, N>& values)
        {
            for (auto& value : values)
            {
                Value(value);
            }
        }
        template <class A, class B> void Value(std::pair<A, B>& value)
        {
            Value(value.first);
            Value(value.second);
        }
        void Value(Vector2f& v)
        {
            Value(v.x);
            Value(v.y);
        }
        void Value(Vector3f& v)
        {
            Value(v.x);
            Value(v.y);
            Value(v.z);
        }
        void Value(Vector4f& v)
        {
            Value(v.x);
            Value(v.y);
            Value(v.z);
            Value(v.w);
        }
        template <class T> void Colour(T& v)
        {
            Value(v.red);
            Value(v.green);
            Value(v.blue);
            Value(v.alpha);
        }
        void Value(NumericValue& v)
        {
            Value(v.number);
            Value(v.unit);
        }
        void Value(ColorStop& v)
        {
            Colour(v.color);
            Value(v.position);
        }
        void Value(BoxShadow& v)
        {
            Colour(v.color);
            Value(v.offset_x);
            Value(v.offset_y);
            Value(v.blur_radius);
            Value(v.spread_distance);
            Value(v.inset);
        }
        void Value(Specificity& v)
        {
            Value(v.ids);
            Value(v.classes);
            Value(v.types);
        }
        void Value(CascadePriority& v)
        {
            Value(v.originImportance);
            Value(v.presentationalHint);
            Value(v.inlineStyle);
            Value(v.specificity);
            Value(v.sourceOrder);
        }
        void Value(SourceSpan& v)
        {
            Value(v.byteOffset);
            Value(v.length);
            Value(v.line);
            Value(v.column);
        }
        void Value(HtmlAttribute& v)
        {
            Value(v.name);
            Value(v.value);
            Value(v.span);
        }
        void Value(HtmlNode& v)
        {
            Depth guard(*this);
            Value(v.kind);
            Value(v.tag);
            Value(v.text);
            Value(v.attributes);
            Value(v.children);
            Value(v.start);
            Value(v.end);
            Value(v.localId);
            Value(v.subtreeEnd);
            Value(v.compiledClasses);
            bool has = bool(v.compiledStyle);
            Value(has);
            if (has)
            {
                auto properties = writing ? PropertyDictionary(*v.compiledStyle) : PropertyDictionary{};
                Dictionary(properties, specification);
                if (!writing)
                {
                    v.compiledStyle = MakeShared<PropertyDictionary>(std::move(properties));
                }
            }
        }
        void Value(HtmlDocument& v)
        {
            Value(v.kind);
            Value(v.sourceUrl);
            rebindActive = !writing && rebind && v.kind == HtmlDocumentKind::Fragment;
            Value(v.nodes);
        }
        void Value(Tween& v)
        {
            if (writing && v.callback)
            {
                Fail("A callback timing function cannot be compiled.");
            }
            Value(v.type_in);
            Value(v.type_out);
            Value(v.points);
            Value(v.bezier);
            Value(v.x1);
            Value(v.y1);
            Value(v.x2);
            Value(v.y2);
        }
        void Value(Animation& v)
        {
            Value(v.duration);
            Value(v.tween);
            Value(v.delay);
            Value(v.alternate);
            Value(v.paused);
            Value(v.num_iterations);
            Value(v.name);
        }
        void Value(Transition& v)
        {
            Value(v.id);
            if (!writing)
            {
                v.id = MapId(v.id, propertyScope);
            }
            Value(v.property_name);
            Value(v.tween);
            Value(v.duration);
            Value(v.delay);
            Value(v.reverse_adjustment_factor);
        }
        void Value(TransitionList& v)
        {
            Value(v.none);
            Value(v.all);
            Value(v.transitions);
        }
        void Value(TransformPrimitive& value);
        void VariantValue(Variant& value);
        void PropertyValue(Property& value, const PropertySpecification* scope);
        void Dictionary(PropertyDictionary& value, const PropertySpecification* scope);
        void Specification(SharedPtr<PropertySpecification>& value);
        void Value(AttributeSelector& v)
        {
            Value(v.type);
            Value(v.name);
            Value(v.value);
        }
        void Value(StructuralSelector& value);
        void Value(StructuralSelectorList& values);
        void Value(CompoundSelector& v)
        {
            Value(v.tag);
            Value(v.id);
            Value(v.additional_ids);
            Value(v.class_names);
            Value(v.pseudo_class_names);
            Value(v.attributes);
            Value(v.structural_selectors);
            Value(v.combinator);
        }
        void Node(StyleSheetNode& value, StyleSheetNode* parent, Vector<StyleSheetNode*>* index = nullptr);
        void Sheet(StyleSheet& value);
        void Value(FontFaceRule& v)
        {
            Value(v.family);
            Value(v.sources);
            Value(v.style);
            Value(v.weight);
            Value(v.face_index);
            Value(v.fallback);
        }
        void Document(CompiledDocument& value)
        {
            Specification(value.specification);
            specification = value.specification.get();
            Value(value.html);
            Value(value.title);
            Value(value.parentIdentity);
            bool has = bool(value.styles);
            Value(has);
            if (has)
            {
                if (!writing)
                {
                    value.styles = MakeShared<StyleSheetContainer>();
                }
                auto& container = *value.styles;
                uint32_t count = writing ? Checked(container.media_blocks.size()) : 0;
                Value(count);
                Count(count);
                if (!writing)
                {
                    container.media_blocks.resize(count);
                }
                for (auto& block : container.media_blocks)
                {
                    Dictionary(block.properties, nullptr);
                    Value(block.modifier);
                    if (!writing)
                    {
                        block.stylesheet = SharedPtr<StyleSheet>(new StyleSheet);
                    }
                    Sheet(*block.stylesheet);
                }
                if (!writing)
                {
                    container.property_specifications.push_back(value.specification);
                }
            }
        }

    private:
        bool writing = false;
        Vector<byte>* output = nullptr;
        const byte* data = nullptr;
        size_t remaining = 0;
        uint32_t depth = 0;
        uint32_t items = 0;
        const PropertySpecification* specification = nullptr;
        const PropertySpecification* rebind = nullptr;
        const PropertySpecification* propertyScope = nullptr;
        bool rebindActive = false;
        PropertyId MapId(PropertyId id, const PropertySpecification* scope);
        struct Depth
        {
            CompiledArchive& archive;
            explicit Depth(CompiledArchive& archive) : archive(archive)
            {
                if (++archive.depth > 256)
                {
                    Fail("Compiled nesting limit exceeded.");
                }
            }
            ~Depth()
            {
                --archive.depth;
            }
        };
        [[noreturn]] static void Fail(const char* error)
        {
            throw std::runtime_error(error);
        }
        static uint32_t Checked(size_t size)
        {
            if (size > MaxItems)
            {
                Fail("Compiled collection limit exceeded.");
            }
            return uint32_t(size);
        }
        void Count(uint32_t count)
        {
            if (count > MaxItems || items > MaxItems - count)
            {
                Fail("Compiled item limit exceeded.");
            }
            items += count;
            if (!writing && count > remaining)
            {
                Fail("Truncated compiled collection.");
            }
        }
        void Require(size_t size)
        {
            if (size > remaining)
            {
                Fail("Truncated compiled data.");
            }
        }
        byte Take()
        {
            auto value = *data++;
            --remaining;
            return value;
        }
        void Put(byte value)
        {
            if (output->size() >= MaxBytes)
            {
                Fail("Compiled byte limit exceeded.");
            }
            output->push_back(value);
        }
        template <class Map> auto Keys(const Map& map)
        {
            Vector<typename Map::key_type> keys;
            keys.reserve(map.size());
            for (const auto& item : map)
            {
                keys.push_back(item.first);
            }
            std::sort(keys.begin(), keys.end());
            return keys;
        }
        template <class T> void VariantAs(Variant& value)
        {
            T item = writing ? value.Get<T>() : T{};
            Value(item);
            if (!writing)
            {
                value = std::move(item);
            }
        }
    };
}
