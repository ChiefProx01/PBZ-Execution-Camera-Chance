-- No engine objects here. One random draw per known ordinary-enemy execution.
local Policy = {}
function Policy.classify(difficulty, tags, registeredBoss)
    if registeredBoss then return 'boss' end
    if type(difficulty)=='string' and difficulty:find('Boss',1,true) then return 'boss' end
    for _,tag in ipairs(tags or {}) do if tag=='CharacterType.AI.Boss' then return 'boss' end end
    if difficulty=='CharacterType.Difficulty.Mob'
        or difficulty=='CharacterType.Difficulty.Standard' then return 'normal' end
    return 'unknown'
end
function Policy.new(percent, random)
    percent = tonumber(percent)
    if not percent or percent ~= percent then percent = 15 end
    percent = math.max(0, math.min(100, percent))
    local cache, order = {}, {}
    random = random or math.random
    local self = {}
    function self.setPercent(value)
        local n=tonumber(value)
        if not n or n~=n then n=15 end
        percent=math.max(0,math.min(100,n))
    end
    function self.decide(token, kind)
        if kind == 'boss' then return true, 'boss' end
        if kind ~= 'normal' or not token then return true, 'unclassified' end
        if cache[token] ~= nil then return cache[token], 'cached' end
        local allow
        if percent == 0 then allow = false
        elseif percent == 100 then allow = true
        else allow = random() < percent / 100 end
        cache[token] = allow
        order[#order+1] = token
        if #order > 256 then cache[table.remove(order,1)] = nil end
        return allow, 'rolled'
    end
    return self
end
return Policy
