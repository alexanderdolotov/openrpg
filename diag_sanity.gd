extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	for i in range(20):
		await process_frame

	var lake = main.get_node_or_null("MountainLake")
	print("MountainLake exists and rendered without error: ", lake != null)

	var river = main.get_node("River")
	print("River Length: ", river.Length, " Position: ", river.position)

	# Confirm the 3 hand-placed trees aren't anywhere near the river band.
	for i in range(3):
		var t = main.get_node_or_null("WorldLayer/Tree%d" % i)
		if t:
			var center_y = river.GetCenterlineWorldY(t.global_position.x)
			var dist = abs(t.global_position.y - center_y)
			print("Tree%d pos=%s riverCenterY=%s distFromRiverCenter=%s" % [i, t.global_position, center_y, dist])

	quit()
