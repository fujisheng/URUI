# Standard UPM package design and implementation plan

Scope: local-only conversion of published commit a29a15ebf55b1b26fcfb47bf73fc6105b1d0b563. User clarified Packages, no .unitypackage. The delegated request assigns directory and compatibility choices to this implementer. No publication, Unity upgrade, Android build, research modification or new account access.

## Source audit

Native/bridge.cpp (45,797 bytes) contains the actual RenderInterface, geometry/texture ownership, C ABI, typed transforms, retained element references and native frame/batch integration. Native/{native_frame,prepared_batch,render_plan,core_frame,element_references}.h contain template, inline and in-class method bodies, not only declarations. The other five cpp files are tests. CMake builds bridge.cpp including these headers. The qualified dylib came from release-main/Native and cached RmlUi 6.3 / FreeType 2.13.3 with both instrumentation options OFF. The published source closure matched that qualified source snapshot.

The original project was not a UPM package: no package.json, core runtime referenced Unity.ugui, demo/measurement classes were compiled with core, shader audit paths and Editor tools assumed Assets/RmlUnity. CMake had portable FetchContent tags, but the actual accepted build used external cache overrides. Thus independent clean checkout rebuild had NOT been verified; third-party source was not in the repository. No framework implementation cpp is missing from the published qualified path. Phase2 cache and Phase3 were intentionally excluded, not omitted build inputs. Existing Windows/Linux/Android scripts are not evidence of supported/verified platforms.

