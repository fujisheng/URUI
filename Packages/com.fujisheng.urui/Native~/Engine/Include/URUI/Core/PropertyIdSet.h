#pragma once

#include "ID.h"
#include "Types.h"
#include <array>
#include <bitset>
#include <stdexcept>

namespace URUI {

class PropertyIdSetIterator;

/*
    PropertyIdSet is a 'set'-like container for PropertyIds.

    Implemented as fixed-size words; iteration skips empty words. It is cheap to construct and use, requiring no dynamic allocation.

    Supports union and intersection operations between two sets, as well as iteration through the IDs that are inserted.
*/

class URUI_CORE_API PropertyIdSet {
private:
	static constexpr size_t N = size_t(PropertyId::MaxNumIds);
	static constexpr size_t WordBits = 64;
    static constexpr size_t WordCount = (N + WordBits - 1) / WordBits;
    std::array<uint64_t, WordCount> defined_ids{};
    static size_t CheckedIndex(PropertyId id) {
        const auto index = size_t(id);
        if (index >= N) throw std::out_of_range("PropertyIdSet id");
        return index;
    }
    static unsigned FirstBit(uint64_t word) {
#if defined(__GNUC__) || defined(__clang__)
        return unsigned(__builtin_ctzll(word));
#else
        unsigned bit = 0;
        while ((word & 1) == 0) { word >>= 1; ++bit; }
        return bit;
#endif
    }
    size_t FindNext(size_t from) const {
        if (from >= N) return N;
        size_t index = from / WordBits;
        uint64_t word = defined_ids[index] & (~uint64_t(0) << (from % WordBits));
        for (;;) {
            if (word) {
                const auto result = index * WordBits + FirstBit(word);
                return result < N ? result : N;
            }
            if (++index == WordCount) return N;
            word = defined_ids[index];
        }
    }
    friend class PropertyIdSetIterator;

public:
	PropertyIdSet() { static_assert((size_t)PropertyId::Invalid == 0, "PropertyIdSet makes an assumption that PropertyId::Invalid is zero."); }

	void Insert(PropertyId id)
	{
		URUI_ASSERT(size_t(id) < N);
		const auto index = CheckedIndex(id);
        defined_ids[index / WordBits] |= uint64_t(1) << (index % WordBits);
	}

	void Clear() { defined_ids.fill(0); }
	void Erase(PropertyId id)
	{
		URUI_ASSERT(size_t(id) < N);
		const auto index = CheckedIndex(id);
        defined_ids[index / WordBits] &= ~(uint64_t(1) << (index % WordBits));
	}

	bool Empty() const { for (auto word : defined_ids) if (word) return false; return true; }
	bool Contains(PropertyId id) const { const auto index = CheckedIndex(id); return (defined_ids[index / WordBits] & (uint64_t(1) << (index % WordBits))) != 0; }

	size_t Size() const { size_t result = 0; for (auto word : defined_ids) result += std::bitset<64>(word).count(); return result; }

	// Union with another set
	PropertyIdSet& operator|=(const PropertyIdSet& other)
	{
		for (size_t i = 0; i < WordCount; ++i) defined_ids[i] |= other.defined_ids[i];
		return *this;
	}
	PropertyIdSet operator|(const PropertyIdSet& other) const
	{
		PropertyIdSet result = *this;
		result |= other;
		return result;
	}

	// Intersection with another set
	PropertyIdSet& operator&=(const PropertyIdSet& other)
	{
		for (size_t i = 0; i < WordCount; ++i) defined_ids[i] &= other.defined_ids[i];
		return *this;
	}
	PropertyIdSet operator&(const PropertyIdSet& other) const
	{
		PropertyIdSet result;
		for (size_t i = 0; i < WordCount; ++i) result.defined_ids[i] = defined_ids[i] & other.defined_ids[i];
		return result;
	}

	// Iterator support. Iterates through all the PropertyIds that are set (contained).
	// @note: Only const_iterators are provided.
	inline PropertyIdSetIterator begin() const;
	inline PropertyIdSetIterator end() const;

	// Erases the property id represented by a valid iterator. Iterator must be in the range [begin, end).
	// @return A new iterator pointing to the next element or end().
	inline PropertyIdSetIterator Erase(PropertyIdSetIterator it);
};

class URUI_CORE_API PropertyIdSetIterator {
public:
	PropertyIdSetIterator() = default;
	PropertyIdSetIterator(const PropertyIdSet* container, size_t id_index) : container(container), id_index(id_index) { ProceedToNextValid(); }

	PropertyIdSetIterator& operator++()
	{
		++id_index;
		ProceedToNextValid();
		return *this;
	}

	bool operator==(const PropertyIdSetIterator& other) const { return container == other.container && id_index == other.id_index; }
	bool operator!=(const PropertyIdSetIterator& other) const { return !(*this == other); }
	PropertyId operator*() const { return static_cast<PropertyId>(id_index); }

private:
	inline void ProceedToNextValid()
	{
        id_index = container->FindNext(id_index);
	}

	const PropertyIdSet* container = nullptr;
	size_t id_index = 0;

	friend PropertyIdSetIterator PropertyIdSet::Erase(PropertyIdSetIterator);
};

PropertyIdSetIterator PropertyIdSet::begin() const
{
	if (Empty())
		return end();
	return PropertyIdSetIterator(this, 1);
}

PropertyIdSetIterator PropertyIdSet::end() const
{
	return PropertyIdSetIterator(this, N);
}

PropertyIdSetIterator PropertyIdSet::Erase(PropertyIdSetIterator it)
{
	URUI_ASSERT(it.container == this && it.id_index < N);
	Erase(static_cast<PropertyId>(it.id_index));
	++it;
	return it;
}

} // namespace URUI
