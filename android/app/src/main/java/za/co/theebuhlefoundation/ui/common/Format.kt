// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.common

import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * Dates and numbers, formatted once for the whole app.
 *
 * South African locale throughout: "Sat 26 Sep", not "Sep 26". The API sends
 * ISO-8601 with an offset, which java.time parses without a third-party
 * library — the reason minSdk is 26.
 */
object Format {

    private val southAfrica = Locale.forLanguageTag("en-ZA")

    private val dayAndMonth = DateTimeFormatter.ofPattern("EEE d MMM", southAfrica)
    private val fullDate = DateTimeFormatter.ofPattern("d MMMM yyyy", southAfrica)
    private val shortDate = DateTimeFormatter.ofPattern("d MMM yyyy", southAfrica)
    private val timeOfDay = DateTimeFormatter.ofPattern("HH:mm", southAfrica)

    fun dayMonth(iso: String?): String = parse(iso)?.format(dayAndMonth) ?: EM_DASH

    fun date(iso: String?): String = parse(iso)?.format(shortDate) ?: EM_DASH

    fun longDate(iso: String?): String = parse(iso)?.format(fullDate) ?: EM_DASH

    fun time(iso: String?): String = parse(iso)?.format(timeOfDay) ?: EM_DASH

    /** "Sat 26 Sep · 09:00 – 12:30" */
    fun sessionWhen(startsAt: String?, endsAt: String?): String {
        val start = parse(startsAt) ?: return EM_DASH
        val end = parse(endsAt)

        return buildString {
            append(start.format(dayAndMonth))
            append(" · ")
            append(start.format(timeOfDay))
            if (end != null) {
                append(" – ")
                append(end.format(timeOfDay))
            }
        }
    }

    /** A plain date, as the API sends for `occurredOn` and `dueDate`. */
    fun plainDate(iso: String?): String =
        iso?.let { runCatching { LocalDate.parse(it).format(shortDate) }.getOrNull() } ?: EM_DASH

    /**
     * A mark movement, with its sign always shown.
     *
     * "+11.4" and "−2.0". A subject that has gone backwards is the one a parent
     * most needs to see, so the negative is never hidden or rounded away.
     */
    fun improvement(value: Double): String = when {
        value > 0 -> "+%.1f".format(southAfrica, value)
        value < 0 -> "−%.1f".format(southAfrica, -value)
        else -> "0.0"
    }

    fun percent(value: Int): String = "$value%"

    private fun parse(iso: String?): OffsetDateTime? =
        iso?.let { runCatching { OffsetDateTime.parse(it) }.getOrNull() }

    private const val EM_DASH = "—"
}
