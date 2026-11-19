#pragma once

#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Decorator.h"
#include "../../Include/URUI/Core/Vertex.h"

namespace URUI
{

    class Texture;
    struct Mesh;

    /**
    Base class for tiled decorators.
 */

    class DecoratorTiled : public Decorator
    {
    public:
        DecoratorTiled();
        virtual ~DecoratorTiled();

        /**
	    Stores the orientation of a tile.
	 */
        enum TileOrientation
        {
            // No orientation.
            ORIENTATION_NONE,
            // Flipped horizontally.
            FLIP_HORIZONTAL,
            // Flipped vertically.
            FLIP_VERTICAL,
            // Rotated 180 degrees clockwise.
            ROTATE_180,
        };
        /**
	    Stores the fit mode of a tile.
	 */
        enum TileFitMode
        {
            // Tile is stretched to boundaries.
            FILL,
            // Tile is stretched to boundaries, keeping aspect ratio fixed, 'letter-boxed'.
            CONTAIN,
            // Tile is stretched to cover the boundaries, keeping aspect ratio fixed.
            COVER,
            // Tile is never scaled.
            SCALE_NONE,
            // Tile acts like 'scale-none' if smaller than boundaries, or like 'contain' otherwise.
            SCALE_DOWN,
            // Tile is repeated on both x and y axis
            REPEAT,
            // Tile is repeated on the x axis
            REPEAT_X,
            // Tile is repeated on the y axis
            REPEAT_Y,
        };

        /**
	    Structure for storing the different tiles the tiled decorator uses internally over its
	    surface.
	 */
        struct Tile
        {
            /// Constructs the tile with safe default values.
            Tile();

            /// Calculates the tile's dimensions from the texture and texture coordinates.
            void CalculateDimensions(Texture texture) const;
            /// Get the dimensions (in px) that this tile is ideally displayed as.
            /// URUI images may apply the dp-ratio; standard CSS URLs retain source CSS pixels.
            Vector2f GetNaturalDimensions(Element* element) const;

            /// Generates geometry to render this tile across a surface.
            /// @param[out] vertices The array to store the generated vertex data.
            /// @param[out] indices The array to store the generated index data.
            /// @param[in] computed_values The computed values of the element being decorated.
            /// @param[in] surface_origin The starting point of the first tile to generate.
            /// @param[in] surface_dimensions The dimensions of the surface to be tiled.
            /// @param[in] tile_dimensions The dimensions to render this tile at.
            void GenerateGeometry(Mesh& mesh, const ComputedValues& computed_values, Vector2f surface_origin,
                                  Vector2f surface_dimensions, Vector2f tile_dimensions) const;

            struct TileData
            {
                // 'px' units
                Vector2f size;
                // relative units
                Vector2f texcoords[2];
            };

            int texture_index;

            // Scales the desired displayed size of the tile from raw pixel size. Eg. the 'display_scale' in a sprite sheet.
            float display_scale;
            bool density_scaled = true;

            // Position and size within the texture, absolute units (px)
            Vector2f position;
            Vector2f size;

            mutable TileData tile_data;
            mutable bool tile_data_calculated = false;

            TileOrientation orientation;

            TileFitMode fit_mode;
            Style::LengthPercentage align[2];
        };

    protected:
        enum class Axis
        {
            Horizontal,
            Vertical
        };

        /// Scales a tile dimensions by a fixed value along one axis.
        /// @param tile_dimensions[in, out] The tile dimensions to scale.
        /// @param axis_value[in] The fixed value to scale against.
        /// @param axis[in] The axis to scale against.
        void ScaleTileDimensions(Vector2f& tile_dimensions, float axis_value, Axis axis) const;
    };

    class DecoratorTiledInstancer : public DecoratorInstancer
    {
    public:
        DecoratorTiledInstancer(size_t num_tiles);

    protected:
        /// Adds the property declarations for a tile.
        /// @param[in] name The name of the tile property.
        /// @param[in] register_fit_modes If true, the tile will have the fit modes registered.
        void RegisterTileProperty(const String& name, bool register_fit_modes = false);

        /// Retrieves all the properties for a tile from the property dictionary.
        /// @param[out] tile The tile structure for storing the tile properties.
        /// @param[out] textures Holds the textures declared for the tile.
        /// @param[in] properties The user-defined list of parameters for the decorator.
        /// @param[in] instancer_interface An interface for querying the active style sheet.
        bool GetTileProperties(DecoratorTiled::Tile* tiles, Texture* textures, size_t num_tiles_and_textures,
                               const PropertyDictionary& properties,
                               const DecoratorInstancerInterface& instancer_interface,
                               bool standard_image = false) const;

    private:
        struct TilePropertyIds
        {
            PropertyId src;
            PropertyId fit;
            PropertyId align_x;
            PropertyId align_y;
            PropertyId orientation;
        };

        Vector<TilePropertyIds> tile_property_ids;
    };

}
