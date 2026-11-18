#include "CompiledArchive.h"
#include "DocumentHeader.h"
#include "HtmlBuilder.h"
#include "HtmlParser.h"
#include "StreamFile.h"
#include "StyleSheetParser.h"
#include <URUI/Core/ElementDocument.h>
#include <URUI/Core/StreamMemory.h>
#include <URUI/Core/Utilities.h>

namespace URUI {
void CompiledArchive::Value(TransformPrimitive& value)
{
	auto type = value.type;
	Value(type);
	NumericValue initial[16]{};
#define URUI_COMPILED_TRANSFORM(id, member, name)                       \
	case TransformPrimitive::id:                                        \
	{                                                                   \
		auto item = writing ? value.member : Transforms::name(initial); \
		Value(item.values);                                             \
		if (!writing)                                                   \
			value = TransformPrimitive(item);                           \
		break;                                                          \
	}
	switch (type)
	{
		URUI_COMPILED_TRANSFORM(MATRIX2D, matrix_2d, Matrix2D)
		URUI_COMPILED_TRANSFORM(MATRIX3D, matrix_3d, Matrix3D)
		URUI_COMPILED_TRANSFORM(TRANSLATEX, translate_x, TranslateX)
		URUI_COMPILED_TRANSFORM(TRANSLATEY, translate_y, TranslateY)
		URUI_COMPILED_TRANSFORM(TRANSLATEZ, translate_z, TranslateZ)
		URUI_COMPILED_TRANSFORM(TRANSLATE2D, translate_2d, Translate2D)
		URUI_COMPILED_TRANSFORM(TRANSLATE3D, translate_3d, Translate3D)
		URUI_COMPILED_TRANSFORM(SCALEX, scale_x, ScaleX)
		URUI_COMPILED_TRANSFORM(SCALEY, scale_y, ScaleY)
		URUI_COMPILED_TRANSFORM(SCALEZ, scale_z, ScaleZ)
		URUI_COMPILED_TRANSFORM(SCALE2D, scale_2d, Scale2D)
		URUI_COMPILED_TRANSFORM(SCALE3D, scale_3d, Scale3D)
		URUI_COMPILED_TRANSFORM(ROTATEX, rotate_x, RotateX)
		URUI_COMPILED_TRANSFORM(ROTATEY, rotate_y, RotateY)
		URUI_COMPILED_TRANSFORM(ROTATEZ, rotate_z, RotateZ)
		URUI_COMPILED_TRANSFORM(ROTATE2D, rotate_2d, Rotate2D)
		URUI_COMPILED_TRANSFORM(ROTATE3D, rotate_3d, Rotate3D)
		URUI_COMPILED_TRANSFORM(SKEWX, skew_x, SkewX)
		URUI_COMPILED_TRANSFORM(SKEWY, skew_y, SkewY)
		URUI_COMPILED_TRANSFORM(SKEW2D, skew_2d, Skew2D)
		URUI_COMPILED_TRANSFORM(PERSPECTIVE, perspective, Perspective)
	case TransformPrimitive::DECOMPOSEDMATRIX4:
	{
		auto item = writing ? value.decomposed_matrix_4 : Transforms::DecomposedMatrix4{};
		Value(item.perspective);
		Value(item.quaternion);
		Value(item.translation);
		Value(item.scale);
		Value(item.skew);
		if (!writing)
			value = TransformPrimitive(item);
		break;
	}
	default: Fail("Unknown compiled transform.");
	}
#undef URUI_COMPILED_TRANSFORM
}

void CompiledArchive::VariantValue(Variant& value)
{
	Depth guard(*this);
	uint32_t type = writing ? uint32_t(value.GetType()) : 0;
	Value(type);
	switch (type)
	{
	case Variant::NONE:
		if (!writing)
			value.Clear();
		break;
	case Variant::BOOL: VariantAs<bool>(value); break;
	case Variant::BYTE: VariantAs<byte>(value); break;
	case Variant::CHAR: VariantAs<char>(value); break;
	case Variant::FLOAT: VariantAs<float>(value); break;
	case Variant::DOUBLE: VariantAs<double>(value); break;
	case Variant::INT: VariantAs<int>(value); break;
	case Variant::INT64: VariantAs<int64_t>(value); break;
	case Variant::UINT: VariantAs<unsigned int>(value); break;
	case Variant::UINT64: VariantAs<uint64_t>(value); break;
	case Variant::STRING: VariantAs<String>(value); break;
	case Variant::VECTOR2: VariantAs<Vector2f>(value); break;
	case Variant::VECTOR3: VariantAs<Vector3f>(value); break;
	case Variant::VECTOR4: VariantAs<Vector4f>(value); break;
	case Variant::COLOURF:
	{
		auto item = writing ? value.Get<Colourf>() : Colourf{};
		Colour(item);
		if (!writing)
			value = item;
		break;
	}
	case Variant::COLOURB:
	{
		auto item = writing ? value.Get<Colourb>() : Colourb{};
		Colour(item);
		if (!writing)
			value = item;
		break;
	}
	case Variant::TRANSITIONLIST: VariantAs<TransitionList>(value); break;
	case Variant::ANIMATIONLIST: VariantAs<AnimationList>(value); break;
	case Variant::COLORSTOPLIST: VariantAs<ColorStopList>(value); break;
	case Variant::BOXSHADOWLIST: VariantAs<BoxShadowList>(value); break;
	case Variant::TRANSFORMPTR:
	{
		auto item = writing ? value.Get<TransformPtr>() : TransformPtr{};
		bool has = bool(item);
		Value(has);
		if (has)
		{
			if (!writing)
				item = MakeShared<Transform>();
			uint32_t count = writing ? Checked(item->GetPrimitives().size()) : 0;
			Value(count);
			Count(count);
			if (!writing)
				item->GetPrimitives().assign(count, TransformPrimitive(Transforms::ScaleX(1.f)));
			for (auto& primitive : item->GetPrimitives())
				Value(primitive);
		}
		if (!writing)
			value = item;
		break;
	}
	case Variant::DECORATORSPTR:
	{
		auto source = writing ? value.Get<DecoratorsPtr>() : DecoratorsPtr{};
		bool has = bool(source);
		Value(has);
		SharedPtr<DecoratorDeclarationList> item;
		if (has)
		{
			item = writing ? MakeShared<DecoratorDeclarationList>(*source) : MakeShared<DecoratorDeclarationList>();
			Value(item->value);
			Value(item->image_property);
			uint64_t layers = item->layer_count;
			Value(layers);
			if (layers > MaxItems)
				Fail("Invalid compiled layer count.");
			item->layer_count = size_t(layers);
			uint32_t count = writing ? Checked(item->list.size()) : 0;
			Value(count);
			Count(count);
			if (!writing)
				item->list.resize(count);
			for (auto& entry : item->list)
			{
				Value(entry.type);
				bool anonymous = entry.instancer != nullptr;
				Value(anonymous);
				entry.instancer = anonymous ? Factory::GetDecoratorInstancer(entry.type) : nullptr;
				if (anonymous && !entry.instancer)
					Fail("Unknown compiled decorator.");
				Dictionary(entry.properties, entry.instancer ? &entry.instancer->GetPropertySpecification() : nullptr);
				Value(entry.paint_area);
				uint64_t layer = entry.image_layer;
				Value(layer);
				if (layer > MaxItems)
					Fail("Invalid compiled image layer.");
				entry.image_layer = size_t(layer);
			}
		}
		if (!writing)
			value = DecoratorsPtr(item);
		break;
	}
	case Variant::FILTERSPTR:
	{
		auto source = writing ? value.Get<FiltersPtr>() : FiltersPtr{};
		bool has = bool(source);
		Value(has);
		SharedPtr<FilterDeclarationList> item;
		if (has)
		{
			item = writing ? MakeShared<FilterDeclarationList>(*source) : MakeShared<FilterDeclarationList>();
			Value(item->value);
			uint32_t count = writing ? Checked(item->list.size()) : 0;
			Value(count);
			Count(count);
			if (!writing)
				item->list.resize(count);
			for (auto& entry : item->list)
			{
				Value(entry.type);
				entry.instancer = Factory::GetFilterInstancer(entry.type);
				if (!entry.instancer)
					Fail("Unknown compiled filter.");
				Dictionary(entry.properties, &entry.instancer->GetPropertySpecification());
			}
		}
		if (!writing)
			value = FiltersPtr(item);
		break;
	}
	case Variant::FONTEFFECTSPTR:
	{
		auto source = writing ? value.Get<FontEffectsPtr>() : FontEffectsPtr{};
		bool has = bool(source);
		Value(has);
		SharedPtr<FontEffects> item;
		if (has)
		{
			item = writing ? MakeShared<FontEffects>(*source) : MakeShared<FontEffects>();
			Value(item->value);
			if (writing && item->declarations.empty() && !item->list.empty())
				Fail("Missing compiled font effect declaration.");
			uint32_t count = writing ? Checked(item->declarations.size()) : 0;
			Value(count);
			Count(count);
			if (!writing)
				item->declarations.resize(count);
			for (auto& entry : item->declarations)
			{
				Value(entry.type);
				auto* instancer = Factory::GetFontEffectInstancer(entry.type);
				if (!instancer)
					Fail("Unknown compiled font effect.");
				auto properties = writing ? PropertyDictionary(*entry.properties) : PropertyDictionary{};
				Dictionary(properties, &instancer->GetPropertySpecification());
				if (!writing)
				{
					entry.properties = MakeShared<PropertyDictionary>(properties);
					auto effect = instancer->InstanceFontEffect(entry.type, properties);
					if (!effect)
						Fail("Compiled font effect instancing failed.");
					size_t fingerprint = Hash<String>{}(entry.type);
					for (const auto& pair : properties.GetProperties())
						Utilities::HashCombine(fingerprint, pair.second.ToString());
					effect->SetFingerprint(fingerprint);
					item->list.push_back(std::move(effect));
				}
			}
			if (!writing)
				std::stable_partition(item->list.begin(), item->list.end(),
					[](const auto& effect) { return effect->GetLayer() == FontEffect::Layer::Back; });
		}
		if (!writing)
			value = FontEffectsPtr(item);
		break;
	}
	default: Fail("Non-portable compiled property value.");
	}
}

PropertyId CompiledArchive::MapId(PropertyId id, const PropertySpecification* scope)
{
	if (id == PropertyId::Invalid || !rebindActive || scope != specification)
		return id;
	const auto& name = specification->GetPropertyName(id);
	const auto mapped = name.empty() ? PropertyId::Invalid : rebind->GetPropertyId(name);
	if (mapped == PropertyId::Invalid)
		Fail("Compiled template property is not registered in its owner.");
	return mapped;
}

void CompiledArchive::PropertyValue(Property& value, const PropertySpecification* scope)
{
	Value(value.unit);
	Value(value.priority);
	Value(value.parser_index);
	struct RestoreScope {
		const PropertySpecification*& slot;
		const PropertySpecification* previous;
		~RestoreScope() { slot = previous; }
	} restore{propertyScope, propertyScope};
	propertyScope = scope;
	VariantValue(value.value);
	int id = writing && value.definition ? int(value.definition->GetId()) : -1;
	Value(id);
	if (!writing)
	{
		const auto* target = rebindActive && scope == specification ? rebind : scope;
		value.definition = target && id >= 0 ? target->GetProperty(MapId(PropertyId(id), scope)) : nullptr;
		if (scope && id >= 0 && !value.definition)
			Fail("Unknown compiled property definition.");
	}
	bool has = bool(value.source);
	Value(has);
	if (has)
	{
		auto source = writing ? PropertySource(*value.source) : PropertySource("", 0, "");
		Value(source.path);
		Value(source.line_number);
		Value(source.rule_name);
		if (!writing)
			value.source = MakeShared<PropertySource>(std::move(source));
	}
}
void CompiledArchive::Dictionary(PropertyDictionary& value, const PropertySpecification* scope)
{
	auto keys = Keys(value.GetProperties());
	uint32_t count = writing ? Checked(keys.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		auto id = writing ? keys[i] : PropertyId::Invalid;
		Value(id);
		if (id <= PropertyId::Invalid || id >= PropertyId::MaxNumIds)
			Fail("Invalid compiled property id.");
		auto property = writing ? *value.GetProperty(id) : Property{};
		PropertyValue(property, scope);
		if (!writing)
			value.SetProperty(MapId(id, scope), property);
	}
	auto custom = Keys(value.GetCustomProperties());
	count = writing ? Checked(custom.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		String name = writing ? custom[i] : String{};
		Value(name);
		auto property = writing ? *value.GetCustomProperty(name) : Property{};
		PropertyValue(property, scope);
		if (!writing)
			value.SetCustomProperty(std::move(name), std::move(property));
	}
	auto shorthands = Keys(value.GetVarShorthands());
	count = writing ? Checked(shorthands.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		auto id = writing ? shorthands[i] : ShorthandId::Invalid;
		Value(id);
		if (id == ShorthandId::Invalid)
			Fail("Invalid compiled shorthand.");
		auto property = writing ? *value.GetVarShorthand(id) : Property{};
		PropertyValue(property, scope);
		if (!writing)
			value.SetVarShorthand(id, std::move(property));
	}
}

void CompiledArchive::Specification(SharedPtr<PropertySpecification>& value)
{
	if (!writing)
		value = SharedPtr<PropertySpecification>(StyleSheetSpecification::GetPropertySpecification().Clone());
	const auto* previous = specification;
	specification = value.get();
	Vector<PropertyId> ids;
	if (writing)
		for (auto id : value->GetRegisteredProperties())
			if (value->GetPropertyName(id).compare(0, 2, "--") == 0)
				ids.push_back(id);
	uint32_t count = writing ? Checked(ids.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		auto id = writing ? ids[i] : PropertyId::Invalid;
		Value(id);
		String name = writing ? value->GetPropertyName(id) : String{};
		Value(name);
		if (name.compare(0, 2, "--") != 0 || id <= PropertyId::Invalid || id >= PropertyId::MaxNumIds)
			Fail("Invalid compiled registration.");
		PropertyDefinition* definition = writing ? const_cast<PropertyDefinition*>(value->GetProperty(id)) : nullptr;
		bool inherited = writing ? definition->inherited : false, layout = writing ? definition->forces_layout : false;
		Value(inherited);
		Value(layout);
		if (!writing)
		{
			const auto* existing = value->GetProperty(name);
			if (existing && existing->GetId() != id)
				Fail("Compiled registration id mismatch.");
			definition = existing ? const_cast<PropertyDefinition*>(existing) : &value->RegisterProperty(name, "", inherited, layout, id);
		}
		Value(definition->registration_syntax);
		Value(definition->relative_target);
		uint32_t parsers = writing ? Checked(definition->parsers.size()) : 0;
		Value(parsers);
		Count(parsers);
		if (!writing)
			definition->parsers.resize(parsers);
		for (auto& state : definition->parsers)
		{
			Value(state.name);
			if (!writing)
			{
				state.parser = StyleSheetSpecification::GetParser(state.name);
				if (!state.parser)
					Fail("Unknown compiled property parser.");
			}
			auto keys = Keys(state.parameters);
			uint32_t parameters = writing ? Checked(keys.size()) : 0;
			Value(parameters);
			Count(parameters);
			if (!writing)
				state.parameters.clear();
			for (uint32_t j = 0; j < parameters; j++)
			{
				String key = writing ? keys[j] : String{};
				Value(key);
				int number = writing ? state.parameters.at(key) : 0;
				Value(number);
				if (!writing)
					state.parameters.emplace(std::move(key), number);
			}
		}
		PropertyValue(definition->default_value, nullptr);
		definition->default_value.definition = definition;
	}
	specification = previous;
}

void CompiledArchive::Value(StructuralSelectorList& values)
{
	uint32_t count = writing ? Checked(values.size()) : 0;
	Value(count);
	Count(count);
	if (!writing)
		values.assign(count, StructuralSelector(StructuralSelectorType::Invalid, 0, 0));
	for (auto& value : values)
		Value(value);
}
void CompiledArchive::Value(StructuralSelector& value)
{
	Depth guard(*this);
	Value(value.type);
	Value(value.a);
	Value(value.b);
	Value(value.specificity);
	bool has = bool(value.selector_tree);
	Value(has);
	if (has)
	{
		auto tree = MakeShared<SelectorTree>();
		if (!writing)
			tree->root = MakeUnique<StyleSheetNode>();
		Vector<StyleSheetNode*> index;
		Node(writing ? *value.selector_tree->root : *tree->root, nullptr, &index);
		uint32_t count = writing ? Checked(value.selector_tree->leafs.size()) : 0;
		Value(count);
		Count(count);
		for (uint32_t i = 0; i < count; i++)
		{
			uint32_t leaf = 0;
			if (writing)
			{
				auto found = std::find(index.begin(), index.end(), value.selector_tree->leafs[i]);
				if (found == index.end())
					Fail("Invalid compiled selector leaf.");
				leaf = uint32_t(found - index.begin());
			}
			Value(leaf);
			if (leaf >= index.size())
				Fail("Compiled selector leaf out of range.");
			if (!writing)
				tree->leafs.push_back(index[leaf]);
		}
		if (!writing)
			value.selector_tree = std::move(tree);
	}
}
void CompiledArchive::Node(StyleSheetNode& value, StyleSheetNode* parent, Vector<StyleSheetNode*>* index)
{
	Depth guard(*this);
	if (index)
		index->push_back(&value);
	Value(value.selector);
	Value(value.specificity);
	Dictionary(value.properties, specification);
	uint32_t count = writing ? Checked(value.children.size()) : 0;
	Value(count);
	Count(count);
	if (!writing)
	{
		value.parent = parent;
		value.children.resize(count);
		for (auto& child : value.children)
			child = MakeUnique<StyleSheetNode>();
	}
	for (auto& child : value.children)
		Node(*child, &value, index);
}
void CompiledArchive::Sheet(StyleSheet& value)
{
	Node(*value.root, nullptr);
	Value(value.source_order_offset);
	Value(value.font_faces);
	auto names = Keys(value.keyframes);
	uint32_t count = writing ? Checked(names.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		String name = writing ? names[i] : String{};
		Value(name);
		Keyframes frames = writing ? value.keyframes.at(name) : Keyframes{};
		Value(frames.property_ids);
		uint32_t blocks = writing ? Checked(frames.blocks.size()) : 0;
		Value(blocks);
		Count(blocks);
		if (!writing)
			frames.blocks.assign(blocks, KeyframeBlock(0));
		for (auto& block : frames.blocks)
		{
			Value(block.normalized_time);
			Dictionary(block.properties, specification);
		}
		if (!writing)
			value.keyframes.emplace(std::move(name), std::move(frames));
	}
	names = Keys(value.named_decorator_map);
	count = writing ? Checked(names.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		String name = writing ? names[i] : String{};
		Value(name);
		NamedDecorator decorator = writing ? value.named_decorator_map.at(name) : NamedDecorator{};
		Value(decorator.type);
		decorator.instancer = Factory::GetDecoratorInstancer(decorator.type);
		if (!decorator.instancer)
			Fail("Unknown compiled named decorator.");
		Dictionary(decorator.properties, &decorator.instancer->GetPropertySpecification());
		if (!writing)
			value.named_decorator_map.emplace(std::move(name), std::move(decorator));
	}
	auto& sprites = value.spritesheet_list;
	count = writing ? Checked(sprites.spritesheets.size()) : 0;
	Value(count);
	Count(count);
	for (uint32_t i = 0; i < count; i++)
	{
		String name, source, path;
		int line = 0;
		float scale = 1;
		if (writing)
		{
			const auto& sheet = *sprites.spritesheets[i];
			name = sheet.name;
			source = sheet.texture_source.GetSource();
			path = sheet.texture_source.GetDefinitionSource();
			line = sheet.definition_line_number;
			scale = sheet.display_scale;
		}
		Value(name);
		Value(source);
		Value(path);
		Value(line);
		Value(scale);
		SpriteDefinitionList definitions;
		if (writing)
			for (const auto& pair : sprites.sprite_map)
				if (pair.second.sprite_sheet == sprites.spritesheets[i].get())
					definitions.emplace_back(pair.first, pair.second.rectangle);
		uint32_t size = writing ? Checked(definitions.size()) : 0;
		Value(size);
		Count(size);
		if (!writing)
			definitions.resize(size);
		for (auto& pair : definitions)
		{
			Value(pair.first);
			Value(pair.second.p0);
			Value(pair.second.p1);
		}
		if (!writing && !sprites.AddSpriteSheet(name, source, path, line, scale, definitions))
			Fail("Invalid compiled spritesheet.");
	}
	if (!writing)
		value.BuildNodeIndex();
}

bool CompiledArchive::CompileStyles(const DocumentHeader& header, FileInterface* files, CompiledDocument& compiled, Vector<Diagnostic>& diagnostics)
{
	compiled.styles = MakeShared<StyleSheetContainer>();
	for (const auto& resource : header.css)
	{
		auto sheet = MakeShared<StyleSheetContainer>();
		StyleSheetParser parser(compiled.specification.get(), &diagnostics);
		bool ok = false;
		if (resource.is_inline)
		{
			StreamMemory stream(reinterpret_cast<const byte*>(resource.content.data()), resource.content.size());
			stream.SetSourceURL(resource.path);
			ok = parser.Parse(sheet->media_blocks, &stream, resource.line);
		}
		else
		{
			StreamFile stream(files);
			if (stream.Open(resource.path))
				ok = parser.Parse(sheet->media_blocks, &stream, 1);
		}
		if (!ok)
		{
			diagnostics.push_back({DiagnosticSeverity::Error, "COMPILE_CSS", "Stylesheet compilation failed.", resource.path, {}});
			return false;
		}
		sheet->SetOrigin(resource.origin);
		compiled.styles->MergeStyleSheetContainer(*sheet);
	}
	compiled.styles->property_specifications.push_back(compiled.specification);
	return true;
}

namespace {
	uint64_t ContentHash(const byte* data, size_t size)
	{
		uint64_t hash = 14695981039346656037ull;
		for (size_t i = 0; i < size; i++)
		{
			hash ^= data[i];
			hash *= 1099511628211ull;
		}
		return hash;
	}
	void CompileInline(Vector<HtmlNode>& nodes, const SharedPtr<PropertySpecification>& specification, uint32_t& next)
	{
		for (auto& node : nodes)
		{
			node.localId = next++;
			for (const auto& attribute : node.attributes)
			{
				if (attribute.name == "class")
					StringUtilities::ExpandString(node.compiledClasses, attribute.value, ' ');
				if (attribute.name == "style")
				{
					auto properties = MakeShared<PropertyDictionary>();
					StyleSheetParser parser(specification.get());
					parser.ParseProperties(*properties, attribute.value);
					node.compiledStyle = std::move(properties);
				}
			}
			CompileInline(node.children, specification, next);
			node.subtreeEnd = next;
		}
	}
	void EncodeCompiled(CompiledDocument& compiled, Vector<byte>& output)
	{
		Vector<byte> payload;
		CompiledArchive writer(payload);
		writer.Document(compiled);
		Vector<byte> encoded;
		CompiledArchive header(encoded);
		String magic = "URUI compiled document";
		uint32_t schema = 2, semantics = 1;
		uint64_t size = payload.size(), hash = ContentHash(payload.data(), payload.size());
		header.Value(magic);
		header.Value(schema);
		header.Value(semantics);
		header.Value(size);
		header.Value(hash);
		if (encoded.size() + payload.size() > CompiledArchive::MaxBytes)
			throw std::runtime_error("Compiled byte limit exceeded.");
		encoded.insert(encoded.end(), payload.begin(), payload.end());
		output = std::move(encoded);
	}
} // namespace
bool CompileDocument(const String& html, const String& source, FileInterface* files, Vector<byte>& output, Vector<Diagnostic>& diagnostics,
	bool fragment)
try
{
	auto parsed = fragment ? ParseHtmlFragment(html, "div", source) : ParseHtmlDocument(html, source);
	diagnostics.assign(parsed.diagnostics.begin(), parsed.diagnostics.end());
	if (!parsed)
		return false;
	CompiledDocument compiled;
	compiled.html = std::move(*parsed.document);
	compiled.specification = SharedPtr<PropertySpecification>(StyleSheetSpecification::GetPropertySpecification().Clone());
	if (!fragment)
	{
		DocumentHeader header;
		if (!CollectHtmlHeader(compiled.html, header, diagnostics))
			return false;
		compiled.title = header.title;
		if (!CompiledArchive::CompileStyles(header, files, compiled, diagnostics))
			return false;
	}
	uint32_t next = 0;
	CompileInline(compiled.html.nodes, compiled.specification, next);
	EncodeCompiled(compiled, output);
	return true;
} catch (const std::exception& error)
{
	diagnostics.push_back({DiagnosticSeverity::Error, "COMPILE_ASSET", error.what(), source, {}});
	return false;
}

bool ReadCompiledDocument(const byte* data, size_t size, CompiledDocument& output, String& error, const PropertySpecification* rebind)
try
{
	if (!data || size > CompiledArchive::MaxBytes)
		throw std::runtime_error("Invalid compiled asset size.");
	CompiledArchive header(data, size);
	String magic;
	uint32_t schema = 0, semantics = 0;
	uint64_t length = 0, hash = 0;
	header.Value(magic);
	header.Value(schema);
	header.Value(semantics);
	header.Value(length);
	header.Value(hash);
	if (magic != "URUI compiled document" || schema != 2 || semantics != 1)
		throw std::runtime_error("Compiled asset version mismatch; reimport its source.");
	if (length != header.Remaining())
		throw std::runtime_error("Compiled asset length mismatch.");
	const auto* payload = data + size - size_t(length);
	if (ContentHash(payload, size_t(length)) != hash)
		throw std::runtime_error("Compiled asset checksum mismatch.");
	CompiledDocument compiled;
	CompiledArchive reader(payload, size_t(length), rebind);
	reader.Document(compiled);
	if (!reader.Complete())
		throw std::runtime_error("Trailing compiled asset data.");
	compiled.identity = compiled.html.blueprintIdentity = hash;
	output = std::move(compiled);
	error.clear();
	return true;
} catch (const std::exception& failure)
{
	error = failure.what();
	return false;
}

SharedPtr<const CompiledDocument> CompiledTemplateCache::Read(const byte* data, size_t size, const PropertySpecification& owner, String& error)
{
    if (!data || size == 0) { error = "Invalid compiled template data."; return {}; }
    if (specification != &owner || revision != owner.GetRegisteredPropertyRevision()) {
        Clear(); specification = &owner; revision = owner.GetRegisteredPropertyRevision();
    }
    for (auto it = entries.begin(); it != entries.end(); ++it) {
        if (it->encoded.size() == size && std::memcmp(it->encoded.data(), data, size) == 0) {
            entries.splice(entries.begin(), entries, it);
            error.clear(); return entries.front().plan;
        }
    }
    auto result = MakeShared<CompiledDocument>();
    if (!ReadCompiledDocument(data, size, *result, error, &owner)) { return {}; }
    constexpr size_t maxEntries = 8, maxEncodedBytes = 1024 * 1024;
    if (size <= maxEncodedBytes) {
        while (!entries.empty() && (entries.size() >= maxEntries || encodedBytes + size > maxEncodedBytes)) {
            encodedBytes -= entries.back().encoded.size(); entries.pop_back();
        }
        entries.push_front({Vector<byte>(data, data + size), result});
        encodedBytes += size;
    }
    return result;
}
void CompiledTemplateCache::Clear()
{
    entries.clear(); encodedBytes = 0; specification = nullptr; revision = 0;
}

bool CompileTemplate(const String& html, const String& source, const byte* parent, size_t parent_size, Vector<byte>& output,
	Vector<Diagnostic>& diagnostics)
try
{
	CompiledDocument owner;
	String error;
	if (!ReadCompiledDocument(parent, parent_size, owner, error) || owner.html.kind != HtmlDocumentKind::Document)
	{
		diagnostics.push_back(
			{DiagnosticSeverity::Error, "COMPILE_PARENT", error.empty() ? "Template owner must be a compiled document." : error, source, {}});
		return false;
	}
	auto parsed = ParseHtmlFragment(html, "div", source);
	diagnostics.assign(parsed.diagnostics.begin(), parsed.diagnostics.end());
	if (!parsed)
		return false;
	CompiledDocument compiled;
	compiled.html = std::move(*parsed.document);
	compiled.specification = SharedPtr<PropertySpecification>(owner.specification->Clone());
	compiled.parentIdentity = owner.identity;
	uint32_t next = 0;
	CompileInline(compiled.html.nodes, compiled.specification, next);
	EncodeCompiled(compiled, output);
	return true;
} catch (const std::exception& error)
{
	diagnostics.push_back({DiagnosticSeverity::Error, "COMPILE_TEMPLATE", error.what(), source, {}});
	return false;
}

bool BuildCompiledDocument(ElementDocument& target, CompiledDocument&& compiled, Vector<Diagnostic>& diagnostics)
{
	if (!compiled.specification || compiled.html.kind != HtmlDocumentKind::Document)
		return false;
	if (!BuildHtmlDocument(target, compiled.html, diagnostics, &compiled))
		return false;
	target.SetCompiledIdentity(compiled.identity);
	return true;
}
} // namespace URUI
