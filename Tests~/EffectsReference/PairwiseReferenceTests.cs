using System;using System.IO;using NUnit.Framework;using UnityEngine;
namespace URUI.Effect.Reference {
public sealed class PairwiseReferenceTests {
[Test,Timeout(1800000)] public void CaptureAllModePairsFromLockedSource() {
var output=Path.Combine(Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT"),QualitySettings.activeColorSpace.ToString(),"pairwise-source");PairwiseReferenceCapture.CaptureAll(output);Assert.That(Directory.GetFiles(output,"*.png").Length,Is.EqualTo(126));
Assert.That(File.ReadAllBytes(Path.Combine(output,"Pairwise.000.png")),Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(output,"Pairwise.001.png"))));
}}}