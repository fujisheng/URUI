#include "DocumentFontContext.h"
#include "ControlledLifetimeResource.h"
#include "CssTokenizer.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/FileInterface.h"
#include "../../Include/URUI/Core/FontEngineInterface.h"
#include "../../Include/URUI/Core/ResourceContext.h"
#include "../../Include/URUI/Core/StringUtilities.h"
#include "../../Include/URUI/Core/StyleSheetTypes.h"
#include <limits>
#include <algorithm>
#include <unordered_map>

namespace URUI {
namespace {
struct SourceKey {
	String bytes;
	int index;
	Style::FontStyle style;
	Style::FontWeight weight;
	bool operator==(const SourceKey& other) const {
		return index == other.index && style == other.style && weight == other.weight && bytes == other.bytes;
	}
};
struct SourceHash {
	size_t operator()(const SourceKey& key) const {
		return std::hash<String>{}(key.bytes) ^ (size_t(key.index) << 1) ^ (size_t(key.style) << 9) ^ (size_t(key.weight) << 12);
	}
};
struct Source { String alias; bool loaded = false; };
using ImmutableBytes = SharedPtr<const Vector<byte>>;
struct ImmutableKey {
    const Vector<byte>* bytes;
    int index;
    Style::FontStyle style;
    Style::FontWeight weight;
    bool operator==(const ImmutableKey& other) const {
        return bytes == other.bytes && index == other.index && style == other.style && weight == other.weight;
    }
};
struct ImmutableHash {
    size_t operator()(const ImmutableKey& key) const {
        return std::hash<const void*>{}(key.bytes) ^ (size_t(key.index) << 1) ^ (size_t(key.style) << 9) ^ (size_t(key.weight) << 12);
    }
};
struct ImmutableSource {
    std::weak_ptr<const Vector<byte>> bytes;
    const Source* source;
};
struct DefaultFace { int family; String source; Style::FontStyle style; Style::FontWeight weight; };
struct Sources {
    std::unordered_map<SourceKey, Source, SourceHash> entries;
    std::unordered_map<ImmutableKey, ImmutableSource, ImmutableHash> immutable;
    Vector<DefaultFace> defaults;
    uint64_t next = 0;
};
ControlledLifetimeResource<Sources> sources;

const Source* LoadSource(SourceKey key, bool fallback)
{
    auto found = sources->entries.find(key);
    if (found == sources->entries.end()) {
        if (sources->next == std::numeric_limits<uint64_t>::max()) return nullptr;
        const String alias = "urui-font-source-" + std::to_string(++sources->next);
        found = sources->entries.emplace(std::move(key), Source{alias, false}).first;
        const auto& bytes = found->first.bytes;
        const auto& stored = found->first;
        found->second.loaded = GetFontEngineInterface()->LoadFontFace({reinterpret_cast<const byte*>(bytes.data()), bytes.size()},
            stored.index, alias, stored.style, stored.weight, fallback);
        if (!found->second.loaded) { sources->entries.erase(found); return nullptr; }
    }
    return found->second.loaded ? &found->second : nullptr;
}

const Source* LoadImmutableSource(const ImmutableBytes& bytes, int index, Style::FontStyle style, Style::FontWeight weight)
{
    if (bytes->empty()) { return nullptr; }
    const ImmutableKey key{bytes.get(), index, style, weight};
    auto& cache = sources->immutable;
    const auto found = cache.find(key);
    if (found != cache.end() && found->second.bytes.lock() == bytes) { return found->second.source; }
    const auto* source = LoadSource({String(reinterpret_cast<const char*>(bytes->data()), bytes->size()), index, style, weight}, false);
    if (!source) { return nullptr; }
    // 仅弱持有文件身份，避免字体缓存额外延长页面资源的寿命；加速表保持有界。
    if (cache.size() >= 256) {
        for (auto it = cache.begin(); it != cache.end();) {
            if (it->second.bytes.expired()) { it = cache.erase(it); }
            else { ++it; }
        }
        if (cache.size() >= 256) { cache.erase(cache.begin()); }
    }
    cache.insert_or_assign(key, ImmutableSource{bytes, source});
    return source;
}

int FaceMatchRank(Style::FontStyle candidate_style, Style::FontWeight candidate_weight, Style::FontStyle style, Style::FontWeight weight)
{
    const int requested = int(weight), candidate = int(candidate_weight);
    // CSS searches weight ranges in order, rather than choosing the nearest weight.
    int group;
    if (requested < 400) group = candidate <= requested ? 0 : 1;
    else if (requested > 500) group = candidate >= requested ? 0 : 1;
    else group = candidate >= requested && candidate <= 500 ? 0 : (candidate < requested ? 1 : 2);
    return (candidate_style == style ? 0 : 3000) + group * 1000 + std::abs(candidate - requested);
}

FontFaceHandle DefaultHandle(int family, Style::FontStyle style, Style::FontWeight weight, float size)
{
    const DefaultFace* best = nullptr;
    int best_distance = std::numeric_limits<int>::max();
    for (const auto& face : sources->defaults) {
        if (face.family != family) continue;
        const int distance = FaceMatchRank(face.style, face.weight, style, weight);
        if (distance < best_distance) { best = &face; best_distance = distance; }
    }
    return best ? GetFontEngineInterface()->GetFontFaceHandle(best->source, best->style, weight, size) : 0;
}

int DefaultAlias(const String& family)
{
    if (family == "times" || family == "times new roman" || family == "liberation serif" || family == "urui serif") return 0;
    if (family == "arial" || family == "helvetica" || family == "liberation sans" || family == "urui sans") return 1;
    if (family == "courier" || family == "courier new" || family == "liberation mono" || family == "urui mono") return 2;
    return -1;
}

}

void DocumentFontContext::InitializeSourceCache() { sources.Initialize(); }
void DocumentFontContext::ShutdownSourceCache() { sources.Shutdown(); }

bool DocumentFontContext::LoadDefaultFace(int family, Span<const byte> bytes, Style::FontStyle style, Style::FontWeight weight, int face_index)
{
    if (family < 0 || family > 2 || bytes.empty()) return false;
    SourceKey key{String(reinterpret_cast<const char*>(bytes.data()), bytes.size()), face_index, style, weight};
    for (const auto& face : sources->defaults) if (face.family == family && face.style == style && face.weight == weight) {
        const auto existing = sources->entries.find(key);
        return existing != sources->entries.end() && existing->second.loaded && existing->second.alias == face.source;
    }
    const auto* source = LoadSource(std::move(key), true);
    if (!source) return false;
    sources->defaults.push_back({family, source->alias, style, weight});
    return true;
}

bool DocumentFontContext::SetRules(const Vector<FontFaceRule>& rules)
{
	DocumentFontContext candidate;
	candidate.resource_context = resource_context;
	failed_source.clear();
	for (const auto& rule : rules) {
		bool loaded = false;
		for (const auto& source : rule.sources)
			if (candidate.LoadFace(source, rule.family, rule.style, rule.weight, rule.face_index)) { loaded = true; break; }
		if (!loaded) { failed_source = rule.sources.empty() ? rule.family : rule.sources.front(); return false; }
	}
	if (candidate.faces != faces) { faces.swap(candidate.faces); ++revision; }
	return true;
}

bool DocumentFontContext::LoadFace(const String& path, const String& family, Style::FontStyle style, Style::FontWeight weight, int face_index)
{
	auto* files = resource_context ? resource_context->GetFileInterface() : GetFileInterface();
	const auto handle = files->Open(path);
	if (!handle) return false;
	struct CloseFile { FileInterface* files; FileHandle handle; ~CloseFile() { files->Close(handle); } } close{files, handle};
    const Source* source = nullptr;
    if (const auto bytes = files->GetImmutableData(handle)) {
        source = LoadImmutableSource(bytes, face_index, style, weight);
    } else {
        SourceKey key{String(files->Length(handle), '\0'), face_index, style, weight};
        if (key.bytes.empty() || files->Read(key.bytes.data(), key.bytes.size(), handle) != key.bytes.size()) { return false; }
        source = LoadSource(std::move(key), false);
    }
    if (!source) return false;
    faces.push_back({AsciiLowerCss(family), source->alias, style, weight});
	return true;
}

FontFaceHandle DocumentFontContext::ResolveFace(const String& value, Style::FontStyle style, Style::FontWeight weight, float size) const
{
    auto* engine = GetFontEngineInterface();
    Vector<CssFontFamily> families;
    if (!ParseCssFontFamilies(value, families)) return DefaultHandle(0, style, weight, size);
    Vector<FontFaceHandle> handles;
    auto append = [&](FontFaceHandle handle) {
        if (handle && std::find(handles.begin(), handles.end(), handle) == handles.end()) handles.push_back(handle);
    };
    for (const auto& family : families) {
        if (family.generic >= 0) {
            append(DefaultHandle(family.generic < 3 ? family.generic : 0, style, weight, size));
            continue;
        }
        const Face* best = nullptr;
        int best_distance = std::numeric_limits<int>::max();
        for (auto it = faces.rbegin(); it != faces.rend(); ++it) {
            if (it->family != family.name) continue;
            const int distance = FaceMatchRank(it->style, it->weight, style, weight);
            if (distance < best_distance) { best = &*it; best_distance = distance; }
        }
        auto handle = best ? engine->GetFontFaceHandle(best->source, best->style, weight, size) :
            engine->GetFontFaceHandle(family.name, style, weight, size);
        if (!handle) {
            const int alias = DefaultAlias(family.name);
            if (alias >= 0) handle = DefaultHandle(alias, style, weight, size);
        }
        append(handle);
    }
    if (handles.empty()) append(DefaultHandle(0, style, weight, size));
    return engine->GetFontFaceHandleChain({handles.data(), handles.size()});
}

} // namespace URUI
