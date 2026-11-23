#include "TextureLayoutRow.h"
#include "TextureLayout.h"

namespace URUI
{

    TextureLayoutRow::TextureLayoutRow()
    {
        height = 0;
    }

    TextureLayoutRow::~TextureLayoutRow()
    {
    }

    int TextureLayoutRow::Generate(TextureLayout& layout, int max_width, int y, int* shared_first_unplaced)
    {
        int width = 1;
        const int num_rectangles = layout.GetNumRectangles();
        int scan_start = shared_first_unplaced ? *shared_first_unplaced : 0;
        while (scan_start < num_rectangles && layout.GetRectangle(scan_start).IsPlaced())
        {
            ++scan_start;
        }
        if (shared_first_unplaced)
        {
            *shared_first_unplaced = scan_start;
        }
        int first_unplaced_index = scan_start;
        int placed_rectangles = 0;

        while (width < max_width)
        {
            // Find the first unplaced rectangle we can fit.
            int index;
            for (index = first_unplaced_index; index < num_rectangles; ++index)
            {
                TextureLayoutRectangle& rectangle = layout.GetRectangle(index);
                if (!rectangle.IsPlaced())
                {
                    if (width + rectangle.GetDimensions().x + 1 <= max_width)
                    {
                        break;
                    }
                }
            }

            if (index == num_rectangles)
            {
                return placed_rectangles;
            }

            TextureLayoutRectangle& rectangle = layout.GetRectangle(index);

            // Increment the row height if necessary.
            height = Math::Max(height, rectangle.GetDimensions().y);

            // Add this glyph onto our list and mark it as placed.
            rectangles.push_back(&rectangle);
            rectangle.Place(layout.GetNumTextures(), Vector2i(width, y));
            ++placed_rectangles;

            // Increment our width. An extra pixel is added on so the rectangles aren't pushed up
            // against each other. This will avoid filtering artifacts.
            if (rectangle.GetDimensions().x > 0)
            {
                width += rectangle.GetDimensions().x + 1;
            }

            first_unplaced_index = index + 1;
            if (shared_first_unplaced && index == *shared_first_unplaced)
            {
                int next_unplaced = index + 1;
                while (next_unplaced < num_rectangles && layout.GetRectangle(next_unplaced).IsPlaced())
                {
                    ++next_unplaced;
                }
                *shared_first_unplaced = next_unplaced;
            }
        }

        return placed_rectangles;
    }

    void TextureLayoutRow::Allocate(byte* texture_data, int stride)
    {
        for (size_t i = 0; i < rectangles.size(); ++i)
        {
            rectangles[i]->Allocate(texture_data, stride);
        }
    }

    int TextureLayoutRow::GetHeight() const
    {
        return height;
    }

    void TextureLayoutRow::Unplace()
    {
        for (size_t i = 0; i < rectangles.size(); ++i)
        {
            rectangles[i]->Unplace();
        }
    }

}
