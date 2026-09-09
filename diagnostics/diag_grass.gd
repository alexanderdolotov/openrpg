extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame

	# Confirm grass patches actually got registered.
	var g0 = main.get_node_or_null("WorldLayer/Grass0")
	var g7 = main.get_node_or_null("WorldLayer/Grass7")
	print("Grass0 exists: ", g0 != null, " Grass7 exists: ", g7 != null)
	if g0:
		print("Grass0 initial Count: ", g0.Count)
		print("Grass0 ActionId (expect empty string, meaning no human gather_* action ever matches): '", g0.ActionId, "'")
		var ate1 = g0.AnimalEat()
		var ate2 = g0.AnimalEat()
		var ate3 = g0.AnimalEat() # should fail, only Count=2
		print("AnimalEat results: ", ate1, " ", ate2, " ", ate3, " (expect true true false)")
		print("Grass0 Count after eating: ", g0.Count)

	# Now check berry bush count increased (6 total now, Berry0..Berry5).
	var b5 = main.get_node_or_null("WorldLayer/Berry5")
	print("Berry5 exists (6 total bushes now): ", b5 != null)

	quit()
