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
#include <cstring>
#include <unordered_map>

namespace URUI
{
    namespace
    {
        using ImmutableBytes = SharedPtr<const Vector<byte>>;

        struct ContentDigest
        {
            uint64_t h1 = 0;
            uint64_t h2 = 0;
            size_t size = 0;
            bool operator==(const ContentDigest& other) const
            {
                return size == other.size && h1 == other.h1 && h2 == other.h2;
            }
        };

        ContentDigest HashBytes(const byte* data, size_t size)
        {
            uint64_t h1 = 14695981039346656037ull;
            uint64_t h2 = 1099511628211ull ^ (uint64_t(size) * 0x9e3779b97f4a7c15ull);
            size_t i = 0;
            for (; i + 8 <= size; i += 8)
            {
                uint64_t word = 0;
                std::memcpy(&word, data + i, 8);
                h1 = (h1 ^ word) * 1099511628211ull;
                h2 ^= word + 0x9e3779b97f4a7c15ull + (h2 << 6) + (h2 >> 2);
                h2 *= 0xbf58476d1ce4e5b9ull;
            }
            for (; i < size; ++i)
            {
                h1 = (h1 ^ data[i]) * 1099511628211ull;
                h2 = (h2 ^ (uint64_t(data[i]) << ((i & 7) * 8))) * 0x94d049bb133111ebull;
            }
            h1 ^= h1 >> 33;
            h1 *= 0xff51afd7ed558ccdull;
            h1 ^= h1 >> 33;
            h2 ^= h2 >> 30;
            h2 *= 0xbf58476d1ce4e5b9ull;
            h2 ^= h2 >> 27;
            return {h1, h2, size};
        }

        struct SourceKey
        {
            ContentDigest digest;
            int index;
            Style::FontStyle style;
            Style::FontWeight weight;
            bool operator==(const SourceKey& other) const
            {
                return index == other.index && style == other.style && weight == other.weight && digest == other.digest;
            }
        };
        struct SourceHash
        {
            size_t operator()(const SourceKey& key) const
            {
                return size_t(key.digest.h1 ^ (key.digest.h2 * 0x9e3779b97f4a7c15ull)) ^ (size_t(key.index) << 1) ^
                       (size_t(key.style) << 9) ^ (size_t(key.weight) << 12);
            }
        };
        struct Source
        {
            String alias;
            ImmutableBytes storage;
            bool loaded = false;
        };
        struct ImmutableKey
        {
            const Vector<byte>* bytes;
            int index;
            Style::FontStyle style;
            Style::FontWeight weight;
            bool operator==(const ImmutableKey& other) const
            {
                return bytes == other.bytes && index == other.index && style == other.style && weight == other.weight;
            }
        };
        struct ImmutableHash
        {
            size_t operator()(const ImmutableKey& key) const
            {
                return std::hash<const void*>{}(key.bytes) ^ (size_t(key.index) << 1) ^ (size_t(key.style) << 9) ^
                       (size_t(key.weight) << 12);
            }
        };
        struct ImmutableSource
        {
            std::weak_ptr<const Vector<byte>> bytes;
            const Source* source;
        };
        struct DefaultFace
        {
            int family;
            String source;
            Style::FontStyle style;
            Style::FontWeight weight;
        };
        struct Sources
        {
            std::unordered_map<SourceKey, Source, SourceHash> entries;
            std::unordered_map<ImmutableKey, ImmutableSource, ImmutableHash> immutable;
            Vector<DefaultFace> defaults;
            uint64_t next = 0;
        };
        ControlledLifetimeResource<Sources> sources;

        const Source* LoadSource(const SourceKey& key, Span<const byte> raw, ImmutableBytes shared_bytes, bool fallback)
        {
            auto found = sources->entries.find(key);
            if (found == sources->entries.end())
            {
                if (sources->next == std::numeric_limits<uint64_t>::max())
                {
                    return nullptr;
                }
                if (!shared_bytes)
                {
                    auto copy = MakeShared<Vector<byte>>(raw.begin(), raw.end());
                    shared_bytes = std::move(copy);
                }
                const String alias = "urui-font-source-" + std::to_string(++sources->next);
                found = sources->entries.emplace(key, Source{alias, std::move(shared_bytes), false}).first;
                const auto& bytes = *found->second.storage;
                found->second.loaded = GetFontEngineInterface()->LoadFontFace(
                    {bytes.data(), bytes.size()}, key.index, alias, key.style, key.weight, fallback);
                if (!found->second.loaded)
                {
                    sources->entries.erase(found);
                    return nullptr;
                }
            }
            return found->second.loaded ? &found->second : nullptr;
        }

        const Source* LoadImmutableSource(const ImmutableBytes& bytes, int index, Style::FontStyle style,
                                          Style::FontWeight weight)
        {
            if (bytes->empty())
            {
                return nullptr;
            }
            const ImmutableKey key{bytes.get(), index, style, weight};
            auto& cache = sources->immutable;
            const auto found = cache.find(key);
            if (found != cache.end() && found->second.bytes.lock() == bytes)
            {
                return found->second.source;
            }
            const SourceKey source_key{HashBytes(bytes->data(), bytes->size()), index, style, weight};
            const auto* source = LoadSource(source_key, {bytes->data(), bytes->size()}, bytes, false);
            if (!source)
            {
                return nullptr;
            }
            // 仅弱持有文件身份，避免字体缓存额外延长页面资源的寿命；加速表保持有界。
            if (cache.size() >= 256)
            {
                for (auto it = cache.begin(); it != cache.end();)
                {
                    if (it->second.bytes.expired())
                    {
                        it = cache.erase(it);
                    }
                    else
                    {
                        ++it;
                    }
                }
                if (cache.size() >= 256)
                {
                    cache.erase(cache.begin());
                }
            }
            cache.insert_or_assign(key, ImmutableSource{bytes, source});
            return source;
        }

        int FaceMatchRank(Style::FontStyle candidate_style, Style::FontWeight candidate_weight, Style::FontStyle style,
                          Style::FontWeight weight)
        {
            const int requested = int(weight);
            const int candidate = int(candidate_weight);
            // CSS searches weight ranges in order, rather than choosing the nearest weight.
            int group;
            if (requested < 400)
            {
                group = candidate <= requested ? 0 : 1;
            }
            else if (requested > 500)
            {
                group = candidate >= requested ? 0 : 1;
            }
            else
            {
                group = candidate >= requested && candidate <= 500 ? 0 : (candidate < requested ? 1 : 2);
            }
            return (candidate_style == style ? 0 : 3000) + group * 1000 + std::abs(candidate - requested);
        }

        FontFaceHandle DefaultHandle(int family, Style::FontStyle style, Style::FontWeight weight, float size)
        {
            const DefaultFace* best = nullptr;
            int best_distance = std::numeric_limits<int>::max();
            for (const auto& face : sources->defaults)
            {
                if (face.family != family)
                {
                    continue;
                }
                const int distance = FaceMatchRank(face.style, face.weight, style, weight);
                if (distance < best_distance)
                {
                    best = &face;
                    best_distance = distance;
                }
            }
            return best ? GetFontEngineInterface()->GetFontFaceHandle(best->source, best->style, weight, size) : 0;
        }

        int DefaultAlias(const String& family)
        {
            if (family == "times" || family == "times new roman" || family == "liberation serif" ||
                family == "urui serif")
            {
                return 0;
            }
            if (family == "arial" || family == "helvetica" || family == "liberation sans" || family == "urui sans")
            {
                return 1;
            }
            if (family == "courier" || family == "courier new" || family == "liberation mono" || family == "urui mono")
            {
                return 2;
            }
            return -1;
        }

    }

    void DocumentFontContext::InitializeSourceCache()
    {
        sources.Initialize();
    }
    void DocumentFontContext::ShutdownSourceCache()
    {
        sources.Shutdown();
    }

    bool DocumentFontContext::LoadDefaultFace(int family, Span<const byte> bytes, Style::FontStyle style,
                                              Style::FontWeight weight, int face_index)
    {
        if (family < 0 || family > 2 || bytes.empty())
        {
            return false;
        }
        const SourceKey key{HashBytes(bytes.data(), bytes.size()), face_index, style, weight};
        for (const auto& face : sources->defaults)
        {
            if (face.family == family && face.style == style && face.weight == weight)
            {
                const auto existing = sources->entries.find(key);
                return existing != sources->entries.end() && existing->second.loaded &&
                       existing->second.alias == face.source;
            }
        }
        const auto* source = LoadSource(key, bytes, {}, true);
        if (!source)
        {
            return false;
        }
        sources->defaults.push_back({family, source->alias, style, weight});
        return true;
    }

    bool DocumentFontContext::SetRules(const Vector<FontFaceRule>& rules)
    {
        DocumentFontContext candidate;
        candidate.resource_context = resource_context;
        failed_source.clear();
        for (const auto& rule : rules)
        {
            bool loaded = false;
            for (const auto& source : rule.sources)
            {
                if (candidate.LoadFace(source, rule.family, rule.style, rule.weight, rule.face_index))
                {
                    loaded = true;
                    break;
                }
            }
            if (!loaded)
            {
                failed_source = rule.sources.empty() ? rule.family : rule.sources.front();
                return false;
            }
        }
        if (candidate.faces != faces)
        {
            faces.swap(candidate.faces);
            resolve_cache.clear();
            ++revision;
        }
        return true;
    }

    bool DocumentFontContext::LoadFace(const String& path, const String& family, Style::FontStyle style,
                                       Style::FontWeight weight, int face_index)
    {
        auto* files = resource_context ? resource_context->GetFileInterface() : GetFileInterface();
        const auto handle = files->Open(path);
        if (!handle)
        {
            return false;
        }
        struct CloseFile
        {
            FileInterface* files;
            FileHandle handle;
            ~CloseFile()
            {
                files->Close(handle);
            }
        } close{files, handle};
        const Source* source = nullptr;
        if (const auto bytes = files->GetImmutableData(handle))
        {
            source = LoadImmutableSource(bytes, face_index, style, weight);
        }
        else
        {
            auto buffer = MakeShared<Vector<byte>>(files->Length(handle));
            if (buffer->empty() || files->Read(buffer->data(), buffer->size(), handle) != buffer->size())
            {
                return false;
            }
            const SourceKey key{HashBytes(buffer->data(), buffer->size()), face_index, style, weight};
            source = LoadSource(key, {buffer->data(), buffer->size()}, std::move(buffer), false);
        }
        if (!source)
        {
            return false;
        }
        faces.push_back({AsciiLowerCss(family), source->alias, style, weight});
        resolve_cache.clear();
        return true;
    }

    FontFaceHandle DocumentFontContext::ResolveFace(const String& value, Style::FontStyle style,
                                                    Style::FontWeight weight, float size) const
    {
        uint32_t size_bits = 0;
        std::memcpy(&size_bits, &size, sizeof(size_bits));
        if (size_bits == 0x80000000u)
        {
            size_bits = 0u;
        }
        ResolveKey cache_key{value, style, weight, size_bits};
        if (auto it = resolve_cache.find(cache_key); it != resolve_cache.end())
        {
            return it->second;
        }

        auto* engine = GetFontEngineInterface();
        Vector<CssFontFamily> families;
        if (!ParseCssFontFamilies(value, families))
        {
            const FontFaceHandle resolved = DefaultHandle(0, style, weight, size);
            if (resolved != 0)
            {
                resolve_cache.emplace(std::move(cache_key), resolved);
            }
            return resolved;
        }
        Vector<FontFaceHandle> handles;
        auto append = [&](FontFaceHandle handle)
        {
            if (handle && std::find(handles.begin(), handles.end(), handle) == handles.end())
            {
                handles.push_back(handle);
            }
        };
        for (const auto& family : families)
        {
            if (family.generic >= 0)
            {
                append(DefaultHandle(family.generic < 3 ? family.generic : 0, style, weight, size));
                continue;
            }
            const Face* best = nullptr;
            int best_distance = std::numeric_limits<int>::max();
            for (auto it = faces.rbegin(); it != faces.rend(); ++it)
            {
                if (it->family != family.name)
                {
                    continue;
                }
                const int distance = FaceMatchRank(it->style, it->weight, style, weight);
                if (distance < best_distance)
                {
                    best = &*it;
                    best_distance = distance;
                }
            }
            auto handle = best ? engine->GetFontFaceHandle(best->source, best->style, weight, size)
                               : engine->GetFontFaceHandle(family.name, style, weight, size);
            if (!handle)
            {
                const int alias = DefaultAlias(family.name);
                if (alias >= 0)
                {
                    handle = DefaultHandle(alias, style, weight, size);
                }
            }
            append(handle);
        }
        if (handles.empty())
        {
            append(DefaultHandle(0, style, weight, size));
        }
        const FontFaceHandle resolved = engine->GetFontFaceHandleChain({handles.data(), handles.size()});
        if (resolved != 0)
        {
            resolve_cache.emplace(std::move(cache_key), resolved);
        }
        return resolved;
    }

}
