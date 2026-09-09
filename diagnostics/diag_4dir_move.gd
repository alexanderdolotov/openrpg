extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var sprite = player.get_node("Sprite")

	# Player normally reads real keyboard state, but UpdateSpriteFacing
	# itself only reads Velocity — set it directly (same as the real
	# _PhysicsProcess does after reading input) and call MoveAndSlide,
	# then let the engine's own next physics tick run UpdateSpriteFacing.
	for case in [
		["moving right", Vector2(80, 0), "walk_right"],
		["moving left", Vector2(-80, 0), "walk_left"],
		["moving up (negative Y)", Vector2(0, -80), "walk_up"],
		["moving down (positive Y)", Vector2(0, 80), "walk_down"],
		["diagonal, X-dominant (60,10)", Vector2(60, 10), "walk_right"],
		["diagonal, Y-dominant (10,-60)", Vector2(10, -60), "walk_up"],
	]:
		player.__TestDriveFacing(case[1])
		print(case[0], " -> animation: ", sprite.animation, " (expect ", case[2], ")")

	# Stop and confirm it holds the LAST facing rather than resetting.
	player.__TestDriveFacing(Vector2.ZERO)
	print("stopped after facing up -> animation: ", sprite.animation, " (expect idle_up)")

	quit()
