local my_test = "Hello, World From Server!"

function on_start()
    G.log(my_test)
end

function on_load()
    G.print_info()
    G.log("LOAD SERVER")
end

function on_unload()
    G.log("UNLOAD SERVER")
end