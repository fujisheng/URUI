#include "render_program_parameters.h"
namespace
{
    urui_plan::GeometrySource PlanGeometry(uint64_t id)
    {
        if(!render)return {};
        const auto found=render->meshes.find(id);
        if(found==render->meshes.end())return {};
        const auto& mesh=found->second;
        return {mesh.vertices.data(),mesh.vertices.size(),mesh.indices.data(),mesh.indices.size(),mesh.bounds_valid?mesh.bounds.data():nullptr};
    }
    int PlanAccess()
    {
        if(context_thread!=std::this_thread::get_id())return -3;
        if(!render||!context||!document_registry)return -2;
        if(urui_interaction::callback_depth)return -5;
        render->draw_plan.Invalidate(render->document_generation);
        return 1;
    }
}
extern "C"
{
    int urui_plan_begin(UruiPlanInputPacket* out) try
    {
        if(!out)return -1;*out={};
        int status=PlanAccess();if(status!=1)return status;
        urui_render();
        if(!render->layer_valid)return -8;
        auto& compiler=render->draw_plan;
        const auto size=context->GetDimensions();compiler.Viewport(uint32_t(size.x),uint32_t(size.y));
        auto* paints=compiler.PaintScratch(render->paint_commands.size());
        int count=urui_get_paint_commands(paints,int(render->paint_commands.size()));
        if(count!=int(render->paint_commands.size()))return -8;
        const auto& commands=render->commands;
        if(render->command_uniforms.size()!=commands.size())return -8;
        status=compiler.Begin(render->document_generation,commands.data(),render->command_transforms.data(),uint32_t(commands.size()),
            paints,uint32_t(count),render->layer_commands.data(),uint32_t(render->layer_commands.size()),*out,render->command_uniforms.data());
        if(status!=1)return status;
        // Nothing has been published to managed code yet: every failure revokes the
        // provisional input lease before returning an all-zero packet.
        auto fail=[&](int error){compiler.Release(out->lease);*out={};return error;};
        for(int n=0;n<count;n++)if(paints[n].reserved==1)
        {
            auto owner=document_registry->OwnerOf(paints[n].element);
            if(!owner)return fail(-2);
            status=compiler.SetImageDocument(out->lease,uint32_t(paints[n].command_index),owner);
            if(status!=1)return fail(status);
        }
        uint32_t colors=0;
        for(int n=0;n<count;n++)if(paints[n].reserved==2)
        {
            auto found=render->paint_shaders.find(paints[n].shader);
            if(found==render->paint_shaders.end()||found->second.color_filters.empty()||found->second.color_filters.size()>16)return fail(-2);
            colors+=uint32_t(found->second.color_filters.size());
        }
        auto* color_fields=compiler.ColorScratch(colors);colors=0;
        for(int n=0;n<count;n++)if(paints[n].reserved==2)
            for(const auto& filter:render->paint_shaders.at(paints[n].shader).color_filters)
                color_fields[colors++]={uint32_t(paints[n].command_index),uint32_t(filter.kind),filter.value};
        status=compiler.SetColorFilters(out->lease,color_fields,colors);if(status!=1)return fail(status);
        uint32_t effects=0;
        for(const auto& layer:render->layer_commands)if(layer.kind==6&&!layer.blend)effects++;
        auto* metadata=compiler.EffectScratch(effects);effects=0;
        for(uint32_t n=0;n<render->layer_commands.size();n++)
        {
            const auto& layer=render->layer_commands[n];if(layer.kind!=6||layer.blend)continue;
            const auto found=render->filters.find(layer.mask);if(found==render->filters.end())return fail(-2);
            const auto& filter=found->second;uint64_t element=0;
            if(filter.element)element=element_references.Track(filter.element.get(),filter.element->GetOwnerDocument());
            auto kind=filter.element?3u:filter.name=="blur"?1u:filter.name=="drop-shadow"?2u:0u;
            auto& data=metadata[effects++];
            data={n,filter.document,element,layer.mask,kind,filter.data.sigma,filter.data.offset_x,filter.data.offset_y,filter.data.value};
            data.cacheable=filter.name=="grayscale"||filter.name=="saturate"||filter.name=="brightness"||filter.name=="contrast"||filter.name=="opacity";
            if(element)
            {
                const auto owner=document_registry->OwnerOf(element);
                if(!owner||(filter.document&&filter.document!=owner))return fail(-2);
                data.document=owner;
                float layout[40]{};uint64_t root=0;
                status=urui_effect_layout(owner,element,layout,40,&root);if(status!=1)return fail(status);
                std::copy_n(layout+4,16,data.projection.data());
            }
        }
        status=compiler.SetLayerEffects(out->lease,metadata,effects);
        return status==1?1:fail(status);
    }
    catch(const std::bad_alloc&){if(out&&out->lease&&render)render->draw_plan.Release(out->lease);if(out)*out={};return -4;}
    catch(...){if(out&&out->lease&&render)render->draw_plan.Release(out->lease);if(out)*out={};return -8;}

    int urui_plan_resolve(uint64_t input,const UruiPlanFeedback* feedback,const UruiPlanBindings* resources,UruiPlanPreparePacket* out) try
    {
        if(!out)return -1;*out={};int status=PlanAccess();if(status!=1)return status;
        return render->draw_plan.Resolve(input,feedback,PlanGeometry,*out,resources);
    }
    catch(...){if(out)*out={};return -8;}
    int urui_plan_compile(uint64_t input,const UruiPlanBindings* bindings,UruiRenderPlanPacket* out) try
    {
        if(!out)return -1;*out={};int status=PlanAccess();if(status!=1)return status;
        if(!bindings)return -1;
        return render->draw_plan.Compile(input,*bindings,PlanGeometry,*out);
    }
    catch(...){if(out)*out={};return -8;}
    int urui_plan_geometry(uint64_t lease,uint64_t geometry,UruiGeometryView* out) try
    {
        if(!out)return -1;*out={};int status=PlanAccess();if(status!=1)return status;
        return render->draw_plan.Geometry(lease,geometry,*out);
    }
    catch(...){if(out)*out={};return -8;}
    int urui_plan_image_program(uint64_t input,uint64_t request,uint64_t program,uint64_t owner,uint64_t layout,
        const float* runtime,uint32_t runtime_count,uint32_t fields,const float* bound,uint64_t clock) try
    {
        int status=PlanAccess();if(status!=1)return status;
        if(!owner||!layout||!runtime||runtime_count<2||runtime_count>32||!fields||fields>512)return -1;
        return render->draw_plan.SetImageProgram(input,request,program,runtime_count+fields,
            [&](const UruiPlanPrepareRequest& pending,float* output,uint32_t)
            {
                if(pending.document!=owner||document_registry->OwnerOf(pending.element)!=owner)return -2;
                std::copy_n(runtime,runtime_count*4,output);
                const auto result=urui_program_layout_values(owner,pending.element,layout,output+runtime_count*4,fields);
                if(result==1&&bound)std::copy_n(bound,fields*4,output+runtime_count*4);
                return result;
            },clock);
    }
    catch(...){return -8;}
    int urui_plan_image_geometry(uint64_t input,uint64_t request,const UruiPlanExternalGeometry* geometry,const float* transform) try
    {
        int status=PlanAccess();if(status!=1)return status;if(!geometry)return -1;
        return render->draw_plan.SetImageGeometry(input,request,*geometry,transform);
    }
    catch(...){return -8;}
    int urui_plan_release(uint64_t lease) try
    {
        int status=PlanAccess();if(status!=1)return status;
        return render->draw_plan.Release(lease);
    }
    catch(...){return -8;}
}
