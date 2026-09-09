extends SceneTree

func _initialize():
	# Standalone harness, NOT Main.tscn — loading the full game would
	# spawn real NPCs that immediately start their own LLM calls through
	# this same shared static queue, contaminating the timing.
	var harness = load("res://scripts/QueueTestHarness.cs").new()
	get_root().add_child(harness)
	await process_frame

	# GameSettings.MaxConcurrentLlmRequests defaults to 4 (nothing loads
	# mind.local.json in this isolated harness, so it's the plain C#
	# default — see GameSettings.cs's own comment on why: measured
	# against analytics.local's actual free VRAM). Firing exactly 4
	# synthetic "requests" against a cap of 4 would never actually queue
	# anything — indistinguishable from a queue that doesn't throttle at
	# all. 8 requests (200ms delay each, no real Ollama call) is what
	# actually exercises the throttle: two real batches of 4.
	harness.StartTest(8, 200)
	while not harness.IsDone():
		await process_frame

	var flat = harness.GetResults()
	var intervals = []
	for i in range(0, flat.size(), 2):
		intervals.append([flat[i], flat[i + 1]])
	print("intervals (start_ms, end_ms) for 8 queued calls, cap=4, 200ms each:")
	for iv in intervals:
		print("  ", iv)

	var max_end = 0.0
	for iv in intervals:
		max_end = max(max_end, iv[1])
	var max_overlap = 0
	var t = 0.0
	while t < max_end:
		var overlap = 0
		for iv in intervals:
			if t >= iv[0] and t < iv[1]:
				overlap += 1
		max_overlap = max(max_overlap, overlap)
		t += 5.0
	print("max concurrent overlap observed (expect exactly 4): ", max_overlap)
	print("total time for all 8 (expect ~400ms: 2 batches of 200ms, NOT ~200ms all-at-once or ~1600ms fully serial): ", max_end)

	quit()
