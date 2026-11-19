#pragma once

#include "Texture.h"
#include "Types.h"

namespace URUI
{

    struct Spritesheet;

    struct Sprite
    {
        // in 'px' units
        Rectanglef rectangle;
        const Spritesheet* sprite_sheet;
    };
    // key: sprite name (as given in @spritesheet)
    using SpriteMap = UnorderedMap<String, Sprite>;

    /**
    Spritesheet holds a list of sprite names given in the @spritesheet at-rule in CSS.
 */
    struct Spritesheet
    {
        String name;
        int definition_line_number;
        // The inverse of the 'resolution' spritesheet property.
        float display_scale;
        TextureSource texture_source;

        Spritesheet(const String& name, const String& source, const String& document_path, int definition_line_number,
                    float display_scale);
    };

    // Sprite name and rectangle
    using SpriteDefinitionList = Vector<Pair<String, Rectanglef>>;

    /**
    SpritesheetList holds all the spritesheets and sprites given in a style sheet.
 */
    class SpritesheetList
    {
    public:
        /// Adds a new sprite sheet to the list and inserts all sprites with unique names into the global list.
        bool AddSpriteSheet(const String& name, const String& image_source, const String& definition_source,
                            int definition_line_number, float display_scale,
                            const SpriteDefinitionList& sprite_definitions);

        /// Get a sprite from its name if it exists.
        /// Note: The pointer is invalidated whenever another sprite is added. Do not store it around.
        const Sprite* GetSprite(const String& name) const;

        /// Merge 'other' into this. Sprites in 'other' will overwrite local sprites if they share the same name.
        void Merge(const SpritesheetList& other);

        void Reserve(size_t size_sprite_sheets, size_t size_sprites);
        size_t NumSpriteSheets() const;
        size_t NumSprites() const;

    private:
        friend class CompiledArchive;
        using Spritesheets = Vector<SharedPtr<const Spritesheet>>;

        Spritesheets spritesheets;
        SpriteMap sprite_map;
    };

}
