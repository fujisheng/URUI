#pragma once

#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/PropertyDictionary.h"
#include "../../Include/URUI/Core/PropertyIdSet.h"
#include "../../Include/URUI/Core/Types.h"
#include <optional>
#include <map>

namespace URUI
{

    using ExpandedShorthands = UnorderedMap<ShorthandId, PropertyDictionary>;

    class ElementDefinition;
    class PlainPropertiesIterator;
    class AllPropertiesIterator;
    enum class RelativeTarget;

    enum class PseudoClassState : uint8_t
    {
        Clear = 0,
        Set = 1,
        Override = 2
    };
    using PseudoClassMap = SmallUnorderedMap<String, PseudoClassState>;

    /**
    Manages an element's style and property information.
 */

    class ElementStyle
    {
    public:
        /// Constructor
        /// @param[in] element The element this structure belongs to.
        ElementStyle(Element* element);

        /// Update this definition if required
        uint64_t GetComputedRevision() const
        {
            return computed_revision;
        }
        uint64_t GetVariableResolutionCount() const
        {
            return variable_resolution_count;
        }
        struct SnapshotValue
        {
            PropertyId id;
            uint32_t kind;
            float values[4];
        };
        const Vector<SnapshotValue>* GetSnapshotValueChanges(uint64_t revision) const
        {
            return value_snapshot_revision == revision && computed_revision == revision + 1 ? &value_snapshot_values
                                                                                            : nullptr;
        }
        void AppendCustomPropertyNames(UnorderedSet<String>& names) const;
        void UpdateDefinition();

        /// Sets or removes a pseudo-class on the element.
        /// @param[in] pseudo_class The pseudo class to activate or deactivate.
        /// @param[in] activate True if the pseudo class is to be activated, false to be deactivated.
        /// @param[in] override_class True to activate or deactivate the override state of the pseudo class, for advanced use cases.
        /// @note An overridden pseudo class means that it will act as if activated even when it has been cleared the normal way.
        /// @return True if the pseudo class was changed.
        bool SetPseudoClass(const String& pseudo_class, bool activate, bool override_class = false);
        /// Checks if a specific pseudo-class has been set on the element.
        /// @param[in] pseudo_class The name of the pseudo-class to check for.
        /// @return True if the pseudo-class is set on the element, false if not.
        bool IsPseudoClassSet(const String& pseudo_class) const;
        /// Gets a list of the current active pseudo classes
        const PseudoClassMap& GetActivePseudoClasses() const;

        /// Sets or removes a class on the element.
        /// @param[in] class_name The name of the class to add or remove from the class list.
        /// @param[in] activate True if the class is to be added, false to be removed.
        /// @return True if the class was changed, false otherwise.
        bool SetClass(const String& class_name, bool activate);
        /// Checks if a class is set on the element.
        /// @param[in] class_name The name of the class to check for.
        /// @return True if the class is set on the element, false otherwise.
        bool IsClassSet(const String& class_name) const;
        /// Specifies the entire list of classes for this element. This will replace any others specified.
        /// @param[in] class_names The list of class names to set on the style, separated by spaces.
        void SetClassNames(const String& class_names);
        /// Return the active class list.
        /// @return A string containing all the classes on the element, separated by spaces.
        String GetClassNames() const;
        /// Return the active class list.
        const StringList& GetClassNameList() const;

        /// Sets a local property override on the element to a pre-parsed value.
        /// @param[in] id The ID  of the new property.
        /// @param[in] property The parsed property to set.
        bool SetProperty(PropertyId id, const Property& property);
        void SetPresentationalProperty(PropertyId id, const Property& property);
        void RemovePresentationalProperty(PropertyId id);
        bool SetAnimatedProperty(PropertyId id, const Property& property, bool transition);
        void RemoveAnimatedProperty(PropertyId id, bool transition);
        bool PrepareSequenceBaseline(uint64_t owner, const PropertyIdSet& properties,
                                     PropertyDictionary& baseline) const;
        const PropertyDictionary* GetSequenceBaseline(uint64_t owner) const;
        bool CaptureSequence(uint64_t owner, const PropertyDictionary& baseline);
        bool SetSequenceValues(uint64_t owner, const PropertyDictionary& values);
        void RemoveSequence(uint64_t owner);
        /// Sets a local custom property override on the element to a pre-parsed value.
        /// @param[in] name The name of the new property.
        /// @param[in] property The parsed property to set.
        void SetCustomProperty(const String& name, const Property& property);
        /// Sets a local var expression shorthand override on the element to a pre-parsed value.
        /// @param[in] id The shorthand id.
        /// @param[in] property The var shorthand value with Unit::VAR_EXPRESSION.
        void SetVarShorthand(ShorthandId id, const Property& property);
        /// Removes a local property override on the element; its value will revert to that defined in the style sheet.
        /// @param[in] id The ID of the local property definition to remove.
        void RemoveProperty(PropertyId id);
        /// Removes a local custom property override on the element; its value will revert to that defined in the style sheet.
        /// @param[in] name The name of the local custom property to remove.
        void RemoveCustomProperty(const String& name);
        /// Removes a local var shorthand override on the element.
        void RemoveVarShorthand(ShorthandId id);
        /// Returns one of this element's properties, with lookup into parents for inherited properties.
        /// @param[in] id The ID of the property to fetch the value for.
        /// @return The value of this property for this element, or the default value if none is declared.
        const Property* GetProperty(PropertyId id) const;
        /// Returns one of this element's custom properties, with lookup into parents.
        /// @param[in] name The name of the property to fetch the value for (including "--" prefix).
        /// @return The value of this property for this element, or nullptr if no property exists with the given name.
        const Property* GetCustomProperty(const String& name) const;
        /// Whether the winning unregistered value requires variable substitution.
        bool HasCustomPropertyExpression(const String& name) const;
        /// Returns one of this element's properties.
        /// @param[in] id The ID of the property to fetch the value for.
        /// @return The value of this property for this element, or nullptr if this property has not been explicitly defined for this element.
        const Property* GetLocalProperty(PropertyId id) const;
        /// Returns one of this element's properties, with any variables fully resolved.
        /// @param[in] id The ID of the property to fetch the value for.
        /// @return The value of this property for this element, or nullptr if this property has not been explicitly defined for this element.
        const Property* GetLocalPropertyWithResolvedVariables(PropertyId id) const;
        /// Returns one of this element's custom properties.
        /// @param[in] name The name of the property to fetch the value for (including "--" prefix).
        /// @return The value of this property for this element, or nullptr if this property has not been explicitly defined for this element.
        const Property* GetLocalCustomProperty(const String& name) const;
        /// Returns one of this element's local shorthands.
        /// @param[in]  id The ID of the shorthand to fetch the value for.
        /// @return The value of this property for this element, or nullptr if this property has not been explicitly defined for this element.
        const Property* GetLocalShorthand(ShorthandId id) const;
        /// Returns the local style properties, excluding any properties from local classes.
        const PropertyDictionary& GetLocalStyleProperties() const;

        /// Resolves a numeric value with units of number, percentage, length, or angle to their canonical unit (unit-less, 'px', or 'rad').
        /// @param[in] value The value to be resolved.
        /// @param[in] base_value The value that is scaled by the number or percentage value, if applicable.
        /// @return The resolved value in their canonical unit, or zero if it could not be resolved.
        float ResolveNumericValue(NumericValue value, float base_value) const;
        /// Resolves a property with units of number, length, or percentage to a length in 'px' units.
        /// Numbers and percentages are resolved by scaling the size of the specified target.
        float ResolveRelativeLength(NumericValue value, RelativeTarget relative_target) const;

        /// Mark inherited properties dirty.
        /// Inherited properties will automatically be set when parent inherited properties are changed. However,
        /// some operations may require to dirty these manually, such as when moving an element into another.
        void DirtyInheritedProperties();

        // Sets a single property as dirty.
        void DirtyProperty(PropertyId id);
        /// Dirties all properties with any of the given units (OR-ed together) on the current element (*not* recursive).
        void DirtyPropertiesWithUnits(Units units);
        /// Dirties all properties with any of the given units (OR-ed together) on the current element and recursively on all children.
        void DirtyPropertiesWithUnitsRecursive(Units units);

        /// Returns true if any properties are dirty such that computed values need to be recomputed
        bool AnyPropertiesDirty() const;

        /// Resolves variables and var expression shorthands for a single property taken from a keyframe block.
        /// @param[in] id The property being resolved.
        /// @param[in] property The property value to resolve, sourced from the keyframe block.
        /// @param[in] block_properties The properties of the keyframe block being processed.
        /// @param[in,out] block_substituted_shorthands Lazily populated cache of expanded shorthands for the current block.
        /// @param[out] variable_dependencies The set of variables referenced during resolution. Can be reused to avoid allocations.
        /// @param[out] property_storage Backing storage for a resolved property, the returned result may point to this.
        /// @return The resolved property, or nullptr on failure.
        const Property* ResolveKeyFrameProperty(PropertyId id, const Property* property,
                                                const PropertyDictionary& block_properties,
                                                std::optional<ExpandedShorthands>& block_substituted_shorthands,
                                                SmallUnorderedSet<String>& variable_dependencies,
                                                Property& property_storage);

        /// Turns the local and inherited properties into computed values for this element. These values can in turn be used during the layout procedure.
        /// Must be called in correct order, always parent before its children.
        PropertyIdSet ComputeValues(Style::ComputedValues& values, const Style::ComputedValues* parent_values,
                                    const Style::ComputedValues* document_values, bool values_are_default_initialized,
                                    float dp_ratio, Vector2f vp_dimensions);

        const Property* GetSpecifiedProperty(PropertyId id)
        {
            return GetSpecifiedProperty(GetPropertySources(), id).property;
        }
        const Property* GetSpecifiedCustomProperty(const String& name)
        {
            return GetSpecifiedCustomProperty(GetPropertySources(), name).property;
        }
        const Property* GetSpecifiedShorthand(ShorthandId id)
        {
            return GetSpecifiedShorthand(GetPropertySources(), id).property;
        }

        /// Returns an iterator for iterating the plain local properties of this element.
        /// Note: Modifying the element's style invalidates its iterator.
        PlainPropertiesIterator Iterate() const;
        /// Make an iterator for all properties including custom and shorthand properties.
        UniquePtr<AllPropertiesIterator> IterateAll(Element* filter_inherited_by) const;

        static void Initialize();
        static void Shutdown();

    private:
        void RecomposeSequenceAnimations();
        std::optional<Property> CaptureInlineTransition(PropertyId id) const;
        void ApplyInlineTransition(PropertyId id, const Property& before);
        struct PropertySources
        {
            Element* element;
            const ElementDefinition* definition;
            const PropertyDictionary& inline_properties;
            const PropertyDictionary* element_inline_properties = nullptr;
            const PropertyDictionary* animations = nullptr;
            const PropertyDictionary* transitions = nullptr;
        };
        struct DirtyPropertiesRef
        {
            PropertyIdSet& properties;
            const UnorderedSet<String>& variables;
            const UnorderedSet<ShorthandId>& var_shorthands;
        };
        struct PropertyElementPair
        {
            const Property* property = nullptr;
            Element* element = nullptr;
        };

        PropertySources GetPropertySources() const;
        DirtyPropertiesRef GetDirtyPropertiesRef();

        void DirtyProperties(const PropertyIdSet& properties);

        const Property* ResolveVariables(PropertyId id, const Property* property,
                                         SmallUnorderedSet<String>& variable_dependencies,
                                         Property& property_storage) const;
        bool ValidateVariableReadCache() const;
        const Property* ResolveVariableRead(PropertyId id, const Property* property) const;

        void ComputeValue(Style::ComputedValues& values, float dp_ratio, Vector2f vp_dimensions, float font_size,
                          float document_font_size, bool& dirty_font_face_handle, PropertyId id, const Property* p);

        static const Property* GetLocalProperty(PropertyId id, const PropertyDictionary& inline_properties,
                                                const ElementDefinition* definition,
                                                const PropertyDictionary* animations = nullptr,
                                                const PropertyDictionary* transitions = nullptr,
                                                const PropertyDictionary* hints = nullptr);
        static const Property* GetLocalCustomProperty(const String& name, const PropertyDictionary& inline_properties,
                                                      const ElementDefinition* definition);
        static const Property* GetLocalShorthand(ShorthandId id, const PropertyDictionary& inline_properties,
                                                 const ElementDefinition* definition);

        // Specified property: The property value before being computed. Includes the cascade, excludes variable substitution.
        static PropertyElementPair GetSpecifiedProperty(const PropertySources& sources, PropertyId id);
        static PropertyElementPair GetSpecifiedCustomProperty(const PropertySources& sources, const String& name);
        static PropertyElementPair GetSpecifiedShorthand(const PropertySources& sources, ShorthandId id);

        struct VariableContext;
        static void CollectVariableCycles(const PropertySources&, const String&, VariableContext&);
        enum class SubstitutionResult
        {
            Substituted,
            NoVariablesFound,
            Error
        };
        static SubstitutionResult SubstituteVariableOnce(const PropertySources& sources, const String& value,
                                                         SmallUnorderedSet<String>& variable_dependencies,
                                                         VariableContext& cycle_chain, String& result);
        static bool SubstituteVariables(const PropertySources& sources, String value,
                                        SmallUnorderedSet<String>& variable_dependencies, VariableContext& cycle_chain,
                                        String& result);

        static const Property* ResolveVariables(const PropertySources& sources,
                                                const ExpandedShorthands& substituted_shorthands, PropertyId id,
                                                const Property* property,
                                                SmallUnorderedSet<String>& variable_dependencies,
                                                Property& property_storage);
        static const Property*
        ResolveVariablesWithShorthandExpansion(const PropertySources& sources, PropertyId id, const Property* property,
                                               std::optional<ExpandedShorthands>& substituted_shorthands,
                                               SmallUnorderedSet<String>& variable_dependencies,
                                               Property& property_storage);

        static void ExpandVarShorthands(ExpandedShorthands& out_substituted_shorthands, const PropertySources& sources,
                                        std::optional<DirtyPropertiesRef> dirty_properties);

        static void TransitionPropertyChanges(const PropertySources& sources, PropertyIdSet& changed_properties,
                                              const ElementDefinition* new_definition);

        // Element these properties belong to
        Element* element;

        // The list of classes applicable to this object.
        StringList classes;
        // This element's current pseudo-classes.
        PseudoClassMap pseudo_classes;

        // The definition of this element, provides applicable properties from the stylesheet.
        uint64_t computed_revision = 0;
        mutable uint64_t variable_resolution_count = 0;
        struct VariableScope
        {
            const ElementStyle* style;
            uint64_t revision;
        };
        struct VariableValue
        {
            uint64_t generation = 0;
            Property expression;
            std::optional<Property> property;
        };
        struct VariableReadCache
        {
            Vector<VariableScope> scopes;
            // map 保证后续读取其他属性时不会因扩容而移动已经返回的 Property。
            std::map<PropertyId, VariableValue> values;
            uint64_t generation = 0;
            int viewport_width = 0;
            int viewport_height = 0;
            float dp_ratio = 0;
        };
        mutable UniquePtr<VariableReadCache> variable_cache;
        uint64_t value_snapshot_revision = UINT64_MAX;
        Vector<SnapshotValue> value_snapshot_values;
        uint64_t registered_property_revision = 0;
        uint64_t font_context_revision = UINT64_MAX;
        SharedPtr<const ElementDefinition> definition;
        // Any properties that have been overridden in this element.
        PropertyDictionary presentational_properties;
        PropertyDictionary inline_properties;
        PropertyDictionary animation_properties;
        PropertyDictionary transition_properties;
        struct SequenceLayer
        {
            PropertyDictionary baseline;
            PropertyDictionary values;
        };
        struct SequenceState
        {
            UnorderedMap<uint64_t, SequenceLayer> layers;
            PropertyDictionary composed_animations;
        };
        UniquePtr<SequenceState> sequence_state;
        const PropertyDictionary* ActiveAnimations() const
        {
            return sequence_state && !sequence_state->layers.empty() ? &sequence_state->composed_animations
                                                                     : &animation_properties;
        }
        // Properties computed from shorthands with variables, here resolved.
        ExpandedShorthands expanded_shorthands;

        PropertyIdSet dirty_properties;
        UnorderedSet<String> dirty_variables;
        UnorderedSet<ShorthandId> dirty_var_shorthands;
        bool has_local_var_or_inherit = false;
        bool MayHaveLocalVarOrInherit() const;
    };

}
