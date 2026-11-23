#pragma once
// Private compiler implementation, included after Budget/Buffer/Identity inside urui_plan.
struct LayerDependencyState
{
    Buffer<uint64_t> words;
    uint64_t version = 0;
    bool dynamic = false;
    bool damageBlocked = false;
    InkRegion damage;
    std::array<float, 4> damageRect{};
    explicit LayerDependencyState(Budget* budget) : words(Allocator<uint64_t>(budget))
    {
    }
    LayerDependencyState(LayerDependencyState&&) = default;
    LayerDependencyState& operator=(LayerDependencyState&&) = default;
    void Reset()
    {
        words.clear();
        version = 0;
        dynamic = damageBlocked = false;
        damage = {};
        damageRect = {};
    }
    void Add(uint64_t value)
    {
        words.push_back(value);
        version = 0;
    }
    void Bytes(const void* value, size_t bytes)
    {
        const auto first = words.size();
        words.resize(first + (bytes + 7) / 8, 0);
        if (bytes)
        {
            std::memcpy(words.data() + first, value, bytes);
        }
        version = 0;
    }
};
class LayerDependencyCache
{
    struct Entry
    {
        Buffer<uint64_t> words;
        uint64_t hash = 0;
        uint64_t version = 0;
        uint64_t lastUse = 0;
        explicit Entry(Budget* budget) : words(Allocator<uint64_t>(budget))
        {
        }
        Entry(Entry&&) = default;
        Entry& operator=(Entry&&) = default;
    };
    Budget* budget;
    Buffer<Entry> entries;
    Buffer<uint32_t> table;
    uint64_t generation = 0;
    size_t cursor = 0;
    size_t tombstones = 0;
    static constexpr uint32_t Empty = UINT32_MAX;
    static constexpr uint32_t Tombstone = UINT32_MAX - 1;
    size_t Slot(uint64_t hash) const
    {
        return size_t(hash) & (table.size() - 1);
    }
    void Insert(uint32_t index)
    {
        size_t slot = Slot(entries[index].hash);
        while (table[slot] != Empty && table[slot] != Tombstone)
        {
            slot = (slot + 1) & (table.size() - 1);
        }
        if (table[slot] == Tombstone)
        {
            tombstones--;
        }
        table[slot] = index;
    }

public:
    explicit LayerDependencyCache(Budget* value)
        : budget(value), entries(Allocator<Entry>(value)), table(Allocator<uint32_t>(value))
    {
    }
    void Begin(size_t layers)
    {
        generation++;
        const size_t capacity = std::max(size_t(64), layers * 4 + 16);
        if (entries.size() >= capacity)
        {
            return;
        }
        entries.reserve(capacity);
        while (entries.size() < capacity)
        {
            entries.emplace_back(budget);
        }
        size_t size = 1;
        while (size < capacity * 2)
        {
            size *= 2;
        }
        table.assign(size, Empty);
        tombstones = 0;
        for (uint32_t n = 0; n < entries.size(); n++)
        {
            if (entries[n].version)
            {
                Insert(n);
            }
        }
    }
    uint64_t Version(LayerDependencyState& state)
    {
        if (tombstones >= table.size() / 4)
        {
            std::fill(table.begin(), table.end(), Empty);
            tombstones = 0;
            for (uint32_t n = 0; n < entries.size(); n++)
            {
                if (entries[n].version)
                {
                    Insert(n);
                }
            }
        }
        if (state.version)
        {
            return state.version;
        }
        if (state.dynamic)
        {
            return state.version = Identity();
        }
        uint64_t hash = 1469598103934665603ull;
        for (auto word : state.words)
        {
            hash ^= word;
            hash *= 1099511628211ull;
        }
        size_t slot = Slot(hash);
        for (size_t probe = 0; probe < table.size(); probe++, slot = (slot + 1) & (table.size() - 1))
        {
            const auto index = table[slot];
            if (index == Empty)
            {
                break;
            }
            if (index == Tombstone)
            {
                continue;
            }
            auto& entry = entries[index];
            // Hash only narrows the lookup. Every word must match before reuse.
            if (entry.hash == hash && entry.words == state.words)
            {
                entry.lastUse = generation;
                return state.version = entry.version;
            }
        }
        for (size_t attempt = 0; attempt < entries.size(); attempt++)
        {
            const auto index = uint32_t(cursor++ % entries.size());
            auto& entry = entries[index];
            if (entry.lastUse == generation)
            {
                continue;
            }
            entry.words.reserve(state.words.size());
            if (entry.version)
            {
                size_t old = Slot(entry.hash);
                for (size_t probe = 0; probe < table.size(); probe++, old = (old + 1) & (table.size() - 1))
                {
                    if (table[old] == index)
                    {
                        table[old] = Tombstone;
                        tombstones++;
                        break;
                    }
                    if (table[old] == Empty)
                    {
                        throw std::logic_error("layer dependency index");
                    }
                }
            }
            entry.words = state.words;
            entry.hash = hash;
            entry.version = Identity();
            entry.lastUse = generation;
            Insert(index);
            return state.version = entry.version;
        }
        throw std::bad_alloc();
    }
};
