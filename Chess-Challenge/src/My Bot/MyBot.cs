//#define POSITION
//#define LOG
using ChessChallenge.API;
using System;
using System.Linq;

// TODO: RFP, futility pruning, and LMR: https://discord.com/channels/1132289356011405342/1140697049046724678/1140699400889454703
public class MyBot : IChessBot
{
    // pawn knight bishop rook queen king
    private readonly int[] piecePhase = { 0, 1, 1, 2, 4, 0 },
        PieceValues = { 82, 337, 365, 447, 1025, 0, // Midgame
                        94, 281, 297, 512,  936, 0}; // Endgame

    // Big table packed with data from premade piece square tables
    // Unpack using PackedEvaluationTables[set, rank] = file
    // pesto[64][12] 64 squares with values flipped for white, 12 piecetypes (first 6 mg, last 6 eg) pawn_mg, knight_mg, ..., queen_eg, king_eg
    private readonly int[][] unpackedPestoTables;

    private readonly TTEntry[] transposTable = new TTEntry[0x7FFFFF + 1]; // ref  m_TPTable[zHash & k_TpMask], 4.7 million entries, likely consuming about 151 MB, 01111111_11111111_11111111

    // ulong 1 is WHITE BOTTOM LEFT (A1), MAX VALUE is EVERY TILE (11111....)

    Board board;
    Timer timer;
    Move bestMoveRoot;
    Move[] killers = new Move[2048];

    bool outOfTime() => timer.MillisecondsElapsedThisTurn > timer.MillisecondsRemaining / 30;

	public MyBot()
	{
        unpackedPestoTables = new[] {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        }.Select(packedTable =>
        {
            int pieceType = 0;
            return new System.Numerics.BigInteger(packedTable).ToByteArray().Take(12)
                    .Select((byte square) => (int)((sbyte)square * 1.461) + PieceValues[pieceType++])
                .ToArray();
        }).ToArray();
    }

	public Move Think(Board board, Timer timer)
    {
        this.timer = timer;
        this.board = board;

#if POSITION
        string fen = "rnbqk1nr/1pp1bppp/p3p3/3p4/3PP3/2N2P2/PPPB2PP/R2QKBNR w KQkq - 0 6";
        return printScores(fen, 7);
#endif

        int beta = 1_000_000,
            alpha = -beta,
            depth = 1;
        while (!outOfTime())
		{
			int score = CalcRecursive(depth, 0, alpha, beta, true);

            // checkmate found
            if (score > 5_000)
                break;

            // Aspiration Search
            if (score <= alpha || score >= beta)
            {
#if LOG
                //Console.WriteLine("Research depth: " + depth); //#DEBUG
#endif
                alpha = int.MinValue + 1;
                beta = int.MaxValue;
            }
            else
            {
                depth++;
                alpha = score - 30;
                beta = score + 30;
            }
        }
#if LOG
        Console.WriteLine("  Search depth: " + depth + ", Score: " + transposTable[board.ZobristKey & 0x7FFFFF].eval); //#DEBUG
#endif
        return bestMoveRoot;
	}

    int CalcRecursive(int depth, int ply, int alpha, int beta, bool nullMoveAllowed)
    {
        bool root = ply++ == 0,
            isInCheck = board.IsInCheck(),
            pvNode = beta - alpha > 1;

        if (!root && board.IsRepeatedPosition())
            return 0;

        if (isInCheck)
            depth++;

        ulong zobristKey = board.ZobristKey;
        TTEntry ttEntry = transposTable[zobristKey & 0x7FFFFF];

        // TT cutoffs
        if (ttEntry.zobristHash == zobristKey && !root && ttEntry.depth >= depth && (
			ttEntry.bound == 3 // exact score
            || ttEntry.bound == 2 && ttEntry.eval >= beta // lower bound, fail high
            || ttEntry.bound == 1 && ttEntry.eval <= alpha // upper bound, fail low
        )) return ttEntry.eval;

        bool qsearch = depth <= 0,
            can_futility_prune = false;
        int eval = Eval(),
            bestScore = int.MinValue,
            movesScoreIndex = 0;

        // Quiescence search is in the same function as negamax to save tokens
        if (qsearch)
        {
            bestScore = eval;
            if (bestScore >= beta) 
                return bestScore;
            alpha = Math.Max(alpha, bestScore);
        }
        else if (!pvNode && !isInCheck)
        {
            // Reverse Futility Pruning
            if (depth < 7 && eval - 94 * depth >= beta)
                return eval;
            // Null Move Pruning
            if (nullMoveAllowed && depth > 1 && board.TrySkipTurn()) // TODO: Check Gamephase
            {
                int s = -CalcRecursive(depth - 3, ply, -beta, -alpha, false);
                board.UndoSkipTurn();
                if (s >= beta) return s;
            }
            // Futility Pruning
            can_futility_prune = depth < 6 && eval + 94 * depth <= alpha;
        }

        Span<Move> moves = stackalloc Move[256];
		board.GetLegalMovesNonAlloc(ref moves, qsearch);

        // End cases
        if (!qsearch && moves.Length == 0)
            return isInCheck ? ply - 300_000 : 0;

        // Score moves for sorting
        int[] scores = new int[moves.Length];
        foreach (Move move in moves)
            scores[movesScoreIndex++] =
                move == ttEntry.move ? 1_000_000 : // last best move
                move.IsCapture ? 10_000 * (int)move.CapturePieceType - (int)move.MovePieceType : // MVV-LVA
                killers[ply] == move ? 1_000 : 0;

        Move bestMove = Move.NullMove;
        int score = 0, origAlpha = alpha, i = 0;

        int Search(int nextAlpha) => score = -CalcRecursive(depth - 1, ply, -nextAlpha, -alpha, nullMoveAllowed);

        for (; i < moves.Length; i++)
        {
            // Incrementally sort moves
            for (int j = i + 1; j < moves.Length; j++)
            {
                if (scores[j] > scores[i])
                    (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
            }

			Move move = moves[i];

            bool tactical = move.IsCapture || move.IsPromotion;
            // Futility Pruning
            if (can_futility_prune && !tactical && i > 0)
                continue;

            board.MakeMove(move);
            //PVS
            if (i == 0 || qsearch)
                Search(beta);
            else
            {
                Search(alpha + 1);
                if (score > alpha && score < beta)
                    Search(beta);
            }
            board.UndoMove(move);

			if (score > bestScore)
            {
				bestScore = score;
                bestMove = move;

                if (root) bestMoveRoot = move;

                alpha = Math.Max(alpha, score);
                // Fail soft beta
                if (score >= beta)
                {
                    // Update history tables
                    if (!move.IsCapture)
                    {
                        killers[ply] = move;
                    }
                    break;
                }
            }

#if !POSITION
            if (outOfTime())
                return 1_000_000;
#endif
        }

        // update cache with best move
        transposTable[zobristKey & 0x7FFFFF] = new TTEntry(
            zobristKey,
            bestMove,
            bestScore,
            depth,
            bestScore >= beta ? 3 : bestScore > origAlpha ? 2 : 1
        );
		return bestScore;
	}

	int Eval()
	{
        int mg = 0, // midgame Values
            eg = 0, // endgame Values
            phase = 0, // progress to endgame
            stm = 2,
            p;

        // sum up values, according to white side
        for (; --stm >= 0; mg = -mg, eg = -eg)
        {
            for (p = -1; ++p < 6;)
            {
                ulong pieces = board.GetPieceBitboard((PieceType) p + 1, stm > 0);
                while (pieces > 0)
                {
                    phase += piecePhase[p];
                    int pieceIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref pieces) ^ 56 * stm; // flip for white (tables are for black side)
                    mg += unpackedPestoTables[pieceIndex][p];
                    eg += unpackedPestoTables[pieceIndex][p+6];
                }
            }
        }
        phase = Math.Min(phase, 24);
        // combine scores and flip score depending on side
        return (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
	}

	record struct TTEntry(ulong zobristHash, Move move, int eval, int depth, int bound);

#if POSITION
    public Move printScores(string fenString, int depth)
    {
        board = Board.CreateBoardFromFEN(fenString);
        Console.WriteLine(board.CreateDiagram());
        Move[] moves = board.GetLegalMoves();
        int[][] scores = new int[moves.Length][];

        for (int i = 0; i < moves.Length; i++)
        {
            scores[i] = new int[depth];
        }

        for (int d = 0; d < depth; d++)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                board.MakeMove(moves[i]);
                scores[i][d] = -CalcRecursive(d, 1, -1_000_000, 1_000_000, true);
                board.UndoMove(moves[i]);
            }
        }
        var orderedMoves = moves.Zip(scores).OrderByDescending(tuple => tuple.Second.Last()).Take(10).Select(tuple => tuple.First + " | " + tuple.Second.Select(i => string.Format("{0,11}", i)).Aggregate((first, second) => first + " | " + second)).ToArray();
        Console.WriteLine("Depth: " + depth);
        foreach (string s in orderedMoves)
        {
            Console.WriteLine(s);
        }
        Console.WriteLine("Avarage: " + scores.Select(s => s.Last()).Sum() / moves.Length);
        Console.WriteLine(timer);
        return moves[0];
    }
#endif
}