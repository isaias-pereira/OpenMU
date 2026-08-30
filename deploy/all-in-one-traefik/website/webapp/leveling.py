"""OpenMU experience-to-level conversion."""


def calculate_level_from_experience(experience):
    """
    Calculate character level from experience points.
    Uses OpenMU's experience formula reversed.
    Formula: XP = 10 * (level + 8) * (level - 1)^2 for level < 256
    """
    if experience <= 0:
        return 1

    # Binary search for level (more reliable than solving the cubic equation)
    low, high = 1, 400
    while low < high:
        mid = (low + high + 1) // 2
        if mid < 256:
            xp_needed = 10 * (mid + 8) * (mid - 1) * (mid - 1)
        else:
            base_xp = 10 * (255 + 8) * (255 - 1) * (255 - 1)
            xp_needed = base_xp + (1000 * (mid - 247) * (mid - 256) * (mid - 256))

        if xp_needed <= experience:
            low = mid
        else:
            high = mid - 1

    return low
