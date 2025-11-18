local message = "Client hello!"

function on_load()
    G.log(message)
end

function on_unload()
    G.log("Unload Client")
end

function on_start()
    G.log("Start Client")
end

function on_update(dt)
    G.log(tostring(type(dt)))
    G.log("Client Tick " .. tostring(dt))
end