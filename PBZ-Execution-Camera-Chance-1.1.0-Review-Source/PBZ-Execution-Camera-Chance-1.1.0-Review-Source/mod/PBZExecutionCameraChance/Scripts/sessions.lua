-- Keep a simple-execution lead-in and its paired finisher on one random draw.
local Sessions={}
function Sessions.new(clock)
    clock=clock or os.time
    local groups,serial={},0
    local self={}
    local function merge(a,b)
        if a=='boss' or b=='boss' then return 'boss' end
        if a~='normal' or b~='normal' then return 'unknown' end
        return 'normal'
    end
    function self.resolve(executor,marker,targets,simple,kind)
        local group=groups[executor]
        local now=clock()
        if simple then
            if not group or group.marker~=marker then
                serial=serial+1
                group={marker=marker,token='simple:'..executor..':'..serial,targets={},kind=kind}
                groups[executor]=group
            end
            for _,target in ipairs(targets) do group.targets[target]=true end
            group.time=now
            group.kind=merge(group.kind,kind)
            return group.token,group.kind
        end
        if group and now-group.time<=15 and #targets==1 and group.targets[targets[1]]
            and (not group.finisher or group.finisher==marker) then
            group.finisher=marker
            return group.token,merge(group.kind,kind)
        end
        return 'finisher:'..marker..':'..table.concat(targets,'|'),kind
    end
    return self
end
return Sessions
