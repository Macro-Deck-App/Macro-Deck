namespace MacroDeckHost.Integrations.Obs;

internal static class ObsVolumeScale
{
	internal const double MinimumDecibels = -100;
	internal const double MaximumDecibels = 0;

	internal static double ToDecibels(double multiplier)
		=> multiplier <= 0 ? MinimumDecibels : Math.Max(MinimumDecibels, 20 * Math.Log10(multiplier));

	// OBS shows a multiplier of 0 as -100 dB, so the bottom of the scale writes exactly 0.
	internal static float ToMultiplier(double decibels)
		=> decibels <= MinimumDecibels ? 0f : (float)Math.Pow(10, Math.Min(decibels, MaximumDecibels) / 20.0);
}
