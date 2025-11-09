local my_test = "Hello, World From Client!"

function on_start()
    G.log(my_test)
    G.print_info()
end

function on_load()
    G.print_info()
    G.log("LOAD CLIENT")
end

function on_unload()
    G.log("UNLOAD CLIENT")
end

function on_update(dt)
    local entities = G.get_entities()
    for _, entity in ipairs(entities) do
        G.log(entity)
    end
end