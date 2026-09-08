extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var area1 = main.__TestComputePathGridArea()
	print("initial area (expect roughly the village floor, ~(-250,-250) to (1450,1050)): ", area1)

	# Move a real decor object (already in _decor) far into the
	# frontier and confirm the computed area actually grows to cover it.
	var mountains = main.get_node("MistyMountains")
	mountains.global_position = Vector2(3000, -2000)
	await process_frame

	var area2 = main.__TestComputePathGridArea()
	print("area after moving MistyMountains to (3000,-2000): ", area2)
	print("new area covers that point (expect true): ", area2.has_point(Vector2(3000, -2000)))
	print("cols/rows at CellSize=30 (expect big but not absurd): ", ceil(area2.size.x / 30.0), " x ", ceil(area2.size.y / 30.0))

	quit()
