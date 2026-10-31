# Verified package scope

Unity 6000.3.22f1, macOS ARM64, Built-in/Metal. Validation is functional, not a performance study. No Unity upgrade, Android target execution, registry package download, credential change or GitHub write was performed.

## Native source completeness

An empty build directory was configured and built entirely from Native~/ and its vendored RmlUi 6.3 / FreeType 2.13.3. No FetchContent or machine-local source override was used. The result is byte-identical to the shipped qualified library: SHA-256 `0c9df11f988f807de559f6407a7c6f4e59414b6fd749a8b8bb0dd88792761a8a`. All 87 exported C ABI symbols match. All framework .cpp/.h inputs match the published qualified source snapshot, and 1,671 vendored files match the recorded inventory.

Four standalone C++ contract suites passed with assertions explicitly enabled (-UNDEBUG). The rebuilt actual C ABI passed 1,000 owner-context lifetimes, 14,804 concurrent wrong-owner queries, stale-output rejection and immutable lease survival after context teardown. These new tests were ordinary Release tests; this package conversion did not rerun the earlier ASan/UBSan or full performance matrix.

## Actual Unity validation

- A fresh project installed the package using a local file: dependency; another fresh project physically embedded it under Packages/com.fujisheng.rmlunity. Both imported and compiled successfully.
- Both checked runtime assembly closure (no Unity.ugui, Match3, CPU probe or clock class), physical source resolution for both shader audits, resource shader availability, and plugin importer filtering to OSX ARM64.
- The explicitly imported Hello Rml sample compiled into its own RmlUnity.Samples.Hello assembly. No sample is compiled on default install.
- Both packages-lock files contain only local/embedded and builtin sources. The only explicit engine module dependency is jsonserialize for optional internal diagnostics. The legacy Input API is supplied by the installed engine, not a separate UPM package.
- A Mac ARM64 IL2CPP development Player built successfully. Editor Play Mode and that Player each passed 24-frame smoke checks: managed defaults, DOM and text/style/attribute setters, retained typed reference, legacy draw recording, green pixel readback, explicit NativeFrame activation, and Host teardown.
- The reusable Editor menu was invoked twice in the isolated test project. Existing edited document contents survived; new scene and document paths were unique.

## Resolved validation setup issues

The first sandboxed Unity launch stopped before import because it could not connect to existing local services. Its own session was normally interrupted, then normal scoped escalation was approved. No credentials or security settings were changed. An invalid guessed inputlegacy UPM dependency was corrected against the installed editor catalog. Two first Editor smoke attempts failed on Unity's Search startup index race: Play Mode was requested before the delayed default index creation. Waiting for Editor initialization via delayCall fixed the test sequencing; errors were not suppressed and production code was not changed for this issue. Raw failed attempts remain in the validation evidence outside the package.

Full evidence: the adjacent task workspace's UPM_STATUS.json and upm-evidence/. Package runtime/Editor source bytes were checked against the embedded fixture after validation. The previous project and current research remain preserved. Other platforms/SRPs, fault injection coverage and comparative performance are not newly validated by these smoke checks.
